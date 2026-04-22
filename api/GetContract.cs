using System.Net;
using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns the full extraction record for a contract.
/// GET /api/get-contract?correlationId={id}
/// Admin bypasses ownership check; user role requires matching UserId.
/// Returns 403 (not 404) when the record exists but belongs to a different user.
/// </summary>
public class GetContract
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public GetContract(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("GetContract")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-contract")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.User); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var correlationId = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["correlationId"];
        if (string.IsNullOrWhiteSpace(correlationId))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "correlationId is required");

        var userId  = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var isAdmin = guard.RoleIds.Contains(Roles.Admin);

        var entity = await _table.GetExtractionAsync(correlationId);
        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Not found");

        if (!isAdmin && entity.UserId != userId)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        JsonElement? fields = null;
        if (!string.IsNullOrEmpty(entity.Fields))
        {
            try
            {
                var extraction = JsonSerializer.Deserialize<JsonElement>(entity.Fields);
                // ExtractionResult is serialised with PascalCase property names.
                // ExtractedFields itself is a raw JSON string — deserialise it a second time.
                if (extraction.TryGetProperty("ExtractedFields", out var ef) &&
                    ef.ValueKind == JsonValueKind.String)
                {
                    var inner = ef.GetString();
                    if (!string.IsNullOrEmpty(inner))
                        fields = JsonSerializer.Deserialize<JsonElement>(inner);
                }
            }
            catch (JsonException) { }
        }

        var okRes = req.CreateResponse();
        await okRes.WriteAsJsonAsync(new
        {
            correlationId  = entity.PartitionKey,
            fileName       = entity.FileName,
            uploadedAt     = entity.UploadedAt,
            processedAt    = entity.ProcessedAt,
            status         = entity.Status,
            documentType   = entity.DocumentType,
            triageConfidence = entity.TriageConfidence,
            extractionConfidence = entity.ExtractionConfidence,
            facts = new
            {
                entity.EffectiveDate,
                entity.ExpiryDate,
                entity.NoticeDeadline,
                entity.NoticePeriodDays,
                entity.AutoRenewal,
                entity.PrimaryCounterparty,
                counterpartyNames = JsonList(entity.CounterpartyNames),
                peopleMentioned = JsonList(entity.PeopleMentioned),
                entity.CustomerName,
                entity.AssignmentStartDate,
                entity.AssignmentEndDate,
                entity.PaymentAmount,
                entity.PaymentCurrency,
                entity.PaymentUnit,
                entity.PaymentType,
                entity.PaymentTerms,
                riskFlags = JsonList(entity.RiskFlags),
                missingFields = JsonList(entity.MissingFields),
            },
            review = new
            {
                state = string.IsNullOrWhiteSpace(entity.ReviewState)
                    ? (entity.Status == "pending_review" ? "pending_review" : "approved_by_extraction")
                    : entity.ReviewState,
                entity.ReviewedAt,
                entity.ReviewedBy,
                entity.ReviewNote,
            },
            fields,
        });
        okRes.StatusCode = HttpStatusCode.OK;
        return okRes;
    }

    private static async Task<HttpResponseData> PlainResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }

    private static IReadOnlyList<string> JsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}

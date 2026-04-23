using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Lists all contracts visible to the caller.
/// GET /api/list-contracts
/// Admin receives all records; user role receives only their own (filtered by UserId).
/// Results are sorted by UploadedAt descending.
/// </summary>
public class ListContracts
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public ListContracts(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ListContracts")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "list-contracts")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.User); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var userId  = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var isAdmin = guard.RoleIds.Contains(Roles.Admin);

        var entities = await _table.ListExtractionsAsync(isAdmin ? null : userId);

        var items = entities.Select(e => new
        {
            correlationId = e.PartitionKey,
            fileName      = e.FileName,
            uploadedAt    = e.UploadedAt,
            status        = e.Status,
            documentType  = e.DocumentType,
            effectiveDate = e.EffectiveDate,
            expiryDate    = e.ExpiryDate,
            noticeDeadline = e.NoticeDeadline,
            noticePeriodDays = e.NoticePeriodDays,
            autoRenewal   = e.AutoRenewal,
            primaryCounterparty = e.PrimaryCounterparty,
            customerName  = e.CustomerName,
            assignmentStartDate = e.AssignmentStartDate,
            assignmentEndDate = e.AssignmentEndDate,
            paymentAmount = e.PaymentAmount,
            paymentCurrency = e.PaymentCurrency,
            paymentUnit = e.PaymentUnit,
            paymentType = e.PaymentType,
            paymentTerms = e.PaymentTerms,
            reviewState = string.IsNullOrWhiteSpace(e.ReviewState)
                ? (e.Status == "pending_review" ? "pending_review" : "approved_by_extraction")
                : e.ReviewState,
            reviewedAt = e.ReviewedAt,
            reviewedBy = e.ReviewedBy,
            relationshipType = e.RelationshipType,
            duplicateOfCorrelationId = e.DuplicateOfCorrelationId,
            supersedesCorrelationId = e.SupersedesCorrelationId,
            relatedContractIds = JsonList(e.RelatedContractIds),
            relationshipReasons = JsonList(e.RelationshipReasons),
            relationshipCandidates = JsonCandidates(e.RelationshipCandidates),
        });

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(items);
        res.StatusCode = HttpStatusCode.OK;
        return res;
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

        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static IReadOnlyList<object> JsonCandidates(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var candidates = System.Text.Json.JsonSerializer.Deserialize<RelationshipCandidateDto[]>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            return candidates.Select(c => new
            {
                correlationId = c.CorrelationId,
                fileName = c.FileName,
                documentType = c.DocumentType,
                relationshipType = c.RelationshipType,
                score = c.Score,
                reasons = c.Reasons,
            }).ToArray();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private record RelationshipCandidateDto(
        string CorrelationId,
        string FileName,
        string DocumentType,
        string RelationshipType,
        int Score,
        IReadOnlyList<string> Reasons);
}

using System.Net;
using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Updates the human review state for an extracted contract.
/// POST /api/review-contract
/// Body: { "correlationId": "...", "action": "approve_as_new", "relatedCorrelationId": "optional", "reviewNote": "optional" }
/// </summary>
public class ReviewContract
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public ReviewContract(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ReviewContract")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "review-contract")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        ReviewContractRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ReviewContractRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Invalid JSON body");
        }

        if (string.IsNullOrWhiteSpace(body?.CorrelationId))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "correlationId is required");

        var decision = NormalizeDecision(body.Action ?? body.ReviewState);
        if (decision is null)
            return await PlainResponse(req, HttpStatusCode.BadRequest, "action must be approve_as_new, reject_delete, mark_duplicate_delete, mark_replacement, or mark_extension");

        if (decision.RequiresRelatedContract && string.IsNullOrWhiteSpace(body.RelatedCorrelationId))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "relatedCorrelationId is required for this action");

        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var entity = await _table.UpdateReviewAsync(
            body.CorrelationId,
            decision.ReviewState,
            userId,
            body.ReviewNote ?? decision.DefaultNote,
            decision.RelationshipType,
            body.RelatedCorrelationId,
            context.CancellationToken);

        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Not found");

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new
        {
            correlationId = entity.PartitionKey,
            status = entity.Status,
            reviewState = entity.ReviewState,
            relationshipType = entity.RelationshipType,
            duplicateOfCorrelationId = entity.DuplicateOfCorrelationId,
            supersedesCorrelationId = entity.SupersedesCorrelationId,
            relatedContractIds = JsonList(entity.RelatedContractIds),
            reviewedAt = entity.ReviewedAt,
            reviewedBy = entity.ReviewedBy,
            deletedAt = entity.DeletedAt,
        });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static ReviewDecision? NormalizeDecision(string? action) =>
        action?.Trim().ToLowerInvariant() switch
        {
            "approved" or "approve" or "approve_as_new" => new("approved", "new", false, "Approved as a new contract."),
            "reject_delete" or "reject-delete" or "reject" => new("rejected", null, false, "Rejected and soft-deleted."),
            "mark_duplicate_delete" or "duplicate_delete" or "duplicate" => new("duplicate_deleted", "duplicate", true, "Marked as duplicate and soft-deleted."),
            "mark_replacement" or "replacement" => new("approved", "replacement", true, "Marked as replacement for an existing contract."),
            "mark_extension" or "extension" => new("approved", "extension", true, "Marked as extension or related new contract."),
            "pending_review" or "pending-review" or "needs_review" => new("pending_review", null, false, "Marked for review."),
            _ => null,
        };

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

public record ReviewContractRequest(
    string CorrelationId,
    string? Action,
    string? ReviewState,
    string? RelatedCorrelationId,
    string? ReviewNote);

public record ReviewDecision(
    string ReviewState,
    string? RelationshipType,
    bool RequiresRelatedContract,
    string DefaultNote);

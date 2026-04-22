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
/// Body: { "correlationId": "...", "reviewState": "approved", "reviewNote": "optional" }
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

        var reviewState = NormalizeReviewState(body.ReviewState);
        if (reviewState is null)
            return await PlainResponse(req, HttpStatusCode.BadRequest, "reviewState must be approved or pending_review");

        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var entity = await _table.UpdateReviewAsync(
            body.CorrelationId,
            reviewState,
            userId,
            body.ReviewNote,
            context.CancellationToken);

        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Not found");

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new
        {
            correlationId = entity.PartitionKey,
            status = entity.Status,
            reviewState = entity.ReviewState,
            reviewedAt = entity.ReviewedAt,
            reviewedBy = entity.ReviewedBy,
        });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static string? NormalizeReviewState(string? reviewState) =>
        reviewState?.Trim().ToLowerInvariant() switch
        {
            "approved" => "approved",
            "pending_review" => "pending_review",
            "pending-review" => "pending_review",
            "needs_review" => "pending_review",
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
}

public record ReviewContractRequest(
    string CorrelationId,
    string ReviewState,
    string? ReviewNote);

using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns the processing status for a contract by correlationId.
/// GET /api/check-status?correlationId={id}
/// Returns { correlationId, status } where status is one of:
/// "processing" | "completed" | "pending_review" | "failed"
/// </summary>
public class CheckStatus
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public CheckStatus(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("CheckStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "check-status")] HttpRequestData req,
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
        {
            var res = req.CreateResponse();
            await res.WriteAsJsonAsync(new { correlationId, status = "processing" });
            res.StatusCode = HttpStatusCode.OK;
            return res;
        }

        if (!isAdmin && entity.UserId != userId)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var okRes = req.CreateResponse();
        await okRes.WriteAsJsonAsync(new
        {
            correlationId,
            status = entity.Status,
            statusMessage = entity.StatusMessage,
            lastError = entity.LastError,
            retryCount = entity.RetryCount,
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
}

using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns the number of entities pending re-embedding. Admin only.
/// GET /api/management-embedding-status
/// </summary>
public class EmbeddingStatus
{
    private readonly IHttpClientFactory      _httpFactory;
    private readonly string                  _appId;
    private readonly EmbeddingsStorageService _embeddingsStorage;

    public EmbeddingStatus(
        IHttpClientFactory       httpFactory,
        IConfiguration           config,
        EmbeddingsStorageService embeddingsStorage)
    {
        _httpFactory       = httpFactory;
        _appId             = config["APP_ID"] ?? "hqagents";
        _embeddingsStorage = embeddingsStorage;
    }

    [Function("ManagementEmbeddingStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "management-embedding-status")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await Plain(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }
        if (!guard.Allowed) return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");

        var pending = await _embeddingsStorage.ListPendingAsync(context.CancellationToken);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { pendingCount = pending.Count });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static async Task<HttpResponseData> Plain(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }
}

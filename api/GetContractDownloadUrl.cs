using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns a short-lived read URL for the original contract blob.
/// GET /api/get-contract-download-url?correlationId={id}
/// </summary>
public class GetContractDownloadUrl
{
    private static readonly TimeSpan LinkTtl = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly BlobStorageService _blobs;
    private readonly string _appId;

    public GetContractDownloadUrl(
        IHttpClientFactory httpFactory,
        TableStorageService table,
        BlobStorageService blobs,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table = table;
        _blobs = blobs;
        _appId = config["APP_ID"] ?? "hqagents";
    }

    [Function("GetContractDownloadUrl")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-contract-download-url")] HttpRequestData req,
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

        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var isAdmin = guard.RoleIds.Contains(Roles.Admin);

        var entity = await _table.GetExtractionAsync(correlationId);
        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Not found");

        if (!isAdmin && entity.UserId != userId)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var url = _blobs.CreateReadSasUri("contracts", entity.BlobPath, LinkTtl);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new
        {
            correlationId = entity.PartitionKey,
            fileName = entity.FileName,
            url = url.ToString(),
            expiresAt = DateTimeOffset.UtcNow.Add(LinkTtl),
        });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static async Task<HttpResponseData> PlainResponse(
        HttpRequestData req,
        HttpStatusCode status,
        string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }
}

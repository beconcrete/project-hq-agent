using System.Net;
using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Hard-deletes a contract from Table Storage and Blob Storage.
/// DELETE /api/contract-delete
/// Body: { "rowKey": "correlationId" }
/// Requires admin role.
/// </summary>
public class ContractDelete
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly BlobStorageService _blob;
    private readonly string _appId;

    public ContractDelete(
        IHttpClientFactory httpFactory,
        TableStorageService table,
        BlobStorageService blob,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _blob        = blob;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ContractDelete")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "contract-delete")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        ContractDeleteRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ContractDeleteRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Invalid JSON body");
        }

        if (string.IsNullOrWhiteSpace(body?.RowKey))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "rowKey is required");

        // Fetch entity first so we can get the BlobPath before deleting
        var entity = await _table.GetExtractionAsync(body.RowKey, context.CancellationToken);
        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Contract not found");

        var blobPath = entity.BlobPath;

        // Delete table row first — if blob delete fails, the record is already gone which is acceptable
        var deleted = await _table.HardDeleteContractAsync(body.RowKey, context.CancellationToken);
        if (!deleted)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Contract not found");

        // Best-effort blob delete — log but don't fail the response
        if (!string.IsNullOrWhiteSpace(blobPath))
        {
            try { await _blob.DeleteAsync("contracts", blobPath, context.CancellationToken); }
            catch (Exception ex)
            {
                // Non-fatal: table row is already gone, blob will be orphaned but that's recoverable
                _ = ex;
            }
        }

        return req.CreateResponse(HttpStatusCode.NoContent);
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

public record ContractDeleteRequest(string RowKey);

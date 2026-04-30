using System.Net;
using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Assigns (or clears) a manual party override on a contract.
/// PATCH /api/contract-assign
/// Body: { "rowKey": "correlationId", "party": "Party name" }
/// Passing an empty party string clears the override.
/// Requires admin role.
/// </summary>
public class ContractAssign
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public ContractAssign(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ContractAssign")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "contract-assign")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        ContractAssignRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ContractAssignRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Invalid JSON body");
        }

        if (string.IsNullOrWhiteSpace(body?.RowKey))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "rowKey is required");

        var entity = await _table.UpdatePartyOverrideAsync(
            body.RowKey,
            body.Party?.Trim() ?? string.Empty,
            context.CancellationToken);

        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Contract not found");

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new
        {
            rowKey              = entity.PartitionKey,
            manualPartyOverride = entity.ManualPartyOverride,
        });
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
}

public record ContractAssignRequest(string RowKey, string? Party);

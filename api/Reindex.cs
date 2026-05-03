using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HqAgent.Api;

/// <summary>
/// Admin-only proxy — validates admin role, forwards to ReindexFunction in
/// hq-agent-function-app which marks all entities pending for re-embedding.
/// POST /api/management-reindex
/// </summary>
public class Reindex
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string             _appId;
    private readonly string             _agentBaseUrl;
    private readonly string             _agentKey;
    private readonly ILogger<Reindex>   _logger;

    public Reindex(IHttpClientFactory httpFactory, IConfiguration config, ILogger<Reindex> logger)
    {
        _httpFactory  = httpFactory;
        _appId        = config["APP_ID"]              ?? "hqagents";
        _agentBaseUrl = config["CHAT_AGENT_BASE_URL"] ?? "";
        _agentKey     = config["CHAT_AGENT_KEY"]      ?? "";
        _logger       = logger;
    }

    [Function("ManagementReindex")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "management-reindex")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await Plain(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }
        if (!guard.Allowed) return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");

        if (string.IsNullOrEmpty(_agentBaseUrl))
        {
            _logger.LogError("CHAT_AGENT_BASE_URL is not configured");
            return await Plain(req, HttpStatusCode.ServiceUnavailable, "Agent not configured");
        }

        using var agentReq = new HttpRequestMessage(HttpMethod.Post,
            $"{_agentBaseUrl}/api/management-reindex");
        agentReq.Headers.Add("x-functions-key", _agentKey);

        var http = _httpFactory.CreateClient();
        using var agentResp = await http.SendAsync(agentReq, context.CancellationToken);
        var body = await agentResp.Content.ReadAsStringAsync(context.CancellationToken);

        if (!agentResp.IsSuccessStatusCode)
            _logger.LogError("Reindex agent returned {Status}: {Body}", (int)agentResp.StatusCode, body);

        var res = req.CreateResponse();
        await res.WriteStringAsync(body);
        res.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        res.StatusCode = agentResp.StatusCode;
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

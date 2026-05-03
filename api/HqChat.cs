using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HqAgent.Api;

/// <summary>
/// Thin proxy — validates the caller, attaches identity headers, and forwards to
/// the HqChatFunction in hq-agent-function-app. The agent URL is kept server-side.
/// POST /api/hq-chat
/// </summary>
public class HqChat
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _appId;
    private readonly string _agentBaseUrl;
    private readonly string _agentKey;
    private readonly ILogger<HqChat> _logger;

    public HqChat(IHttpClientFactory httpFactory, IConfiguration config, ILogger<HqChat> logger)
    {
        _httpFactory  = httpFactory;
        _appId        = config["APP_ID"]              ?? "hqagents";
        _agentBaseUrl = config["CHAT_AGENT_BASE_URL"] ?? "";
        _agentKey     = config["CHAT_AGENT_KEY"]      ?? "";
        _logger       = logger;
    }

    [Function("HqChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hq-chat")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.User); }
        catch { return await Plain(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }
        if (!guard.Allowed) return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");

        if (string.IsNullOrEmpty(_agentBaseUrl))
        {
            _logger.LogError("CHAT_AGENT_BASE_URL is not configured");
            return await Plain(req, HttpStatusCode.ServiceUnavailable, "HQ agent not configured");
        }

        var userId       = context.Items.TryGetValue("userId",       out var uid)  ? uid?.ToString()  ?? "" : "";
        var userEmail    = context.Items.TryGetValue("userEmail",    out var em)   ? em?.ToString()   ?? "" : "";
        var auth0Subject = context.Items.TryGetValue("auth0Subject", out var sub)  ? sub?.ToString()  ?? "" : "";
        var isAdmin      = guard.RoleIds.Contains(Roles.Admin);

        using var sr   = new StreamReader(req.Body);
        var body       = await sr.ReadToEndAsync();

        using var agentReq = new HttpRequestMessage(HttpMethod.Post, $"{_agentBaseUrl}/api/hq-chat");
        agentReq.Headers.Add("x-functions-key",  _agentKey);
        agentReq.Headers.Add("X-User-Id",         userId);
        agentReq.Headers.Add("X-User-Email",       userEmail);
        agentReq.Headers.Add("X-User-Sub",         auth0Subject);
        agentReq.Headers.Add("X-User-Role",        isAdmin ? "admin" : "user");
        agentReq.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var agentResp  = await http.SendAsync(agentReq);
        var responseBody     = await agentResp.Content.ReadAsStringAsync();

        if (!agentResp.IsSuccessStatusCode)
            _logger.LogError("HQ agent returned {Status}: {Body}", (int)agentResp.StatusCode, responseBody);

        var res = req.CreateResponse();
        await res.WriteStringAsync(responseBody);
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

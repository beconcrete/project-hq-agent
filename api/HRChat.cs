using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Thin proxy — validates admin role via RoleGuard, attaches identity headers,
/// and forwards to the HRChat function in hq-agent-function-app.
/// POST /api/hr-chat
/// </summary>
public class HRChat
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string             _appId;
    private readonly string             _agentBaseUrl;
    private readonly string             _agentKey;

    public HRChat(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory  = httpFactory;
        _appId        = config["APP_ID"]              ?? "hqagents";
        _agentBaseUrl = config["CHAT_AGENT_BASE_URL"] ?? "";
        _agentKey     = config["CHAT_AGENT_KEY"]      ?? "";
    }

    [Function("HRChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hr-chat")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await Plain(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }
        if (!guard.Allowed) return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");

        if (string.IsNullOrEmpty(_agentBaseUrl))
            return await Plain(req, HttpStatusCode.ServiceUnavailable, "Chat agent not configured");

        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";

        using var sr = new StreamReader(req.Body);
        var body = await sr.ReadToEndAsync();

        using var agentReq = new HttpRequestMessage(HttpMethod.Post, $"{_agentBaseUrl}/api/hr-chat");
        agentReq.Headers.Add("x-functions-key", _agentKey);
        agentReq.Headers.Add("X-User-Id",       userId);
        agentReq.Headers.Add("X-User-Role",      "admin");
        agentReq.Content = new StringContent(body, Encoding.UTF8);
        agentReq.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var http = _httpFactory.CreateClient();
        using var agentResp = await http.SendAsync(agentReq);

        var responseBody = await agentResp.Content.ReadAsStringAsync();
        var res          = req.CreateResponse();
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

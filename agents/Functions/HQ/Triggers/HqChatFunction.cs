using System.Net;
using System.Text.Json.Serialization;
using HqAgent.Agents.HQ.Agents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HqAgent.Agents.HQ.Triggers;

/// <summary>
/// HTTP-triggered HQ chat endpoint.
/// Called exclusively via the SWA proxy (api/HqChat.cs). AuthorizationLevel.Function
/// prevents direct browser access. Identity is pre-validated by the proxy.
/// POST /api/hq-chat
/// </summary>
public class HqChatFunction
{
    private readonly HqChatAgent _agent;

    public HqChatFunction(HqChatAgent agent) => _agent = agent;

    [Function("HqChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "hq-chat")] HttpRequestData req,
        FunctionContext context)
    {
        var userId       = req.Headers.TryGetValues("X-User-Id",    out var uid)   ? uid.FirstOrDefault()   ?? "" : "";
        var userEmail    = req.Headers.TryGetValues("X-User-Email", out var email) ? email.FirstOrDefault() ?? "" : "";
        var auth0Subject = req.Headers.TryGetValues("X-User-Sub",   out var sub)   ? sub.FirstOrDefault()   ?? "" : "";
        var isAdmin      = req.Headers.TryGetValues("X-User-Role",  out var role)  && role.FirstOrDefault()  == "admin";

        HqChatRequest? body;
        try { body = await System.Text.Json.JsonSerializer.DeserializeAsync<HqChatRequest>(req.Body); }
        catch { return await Plain(req, HttpStatusCode.BadRequest, "Invalid JSON body"); }

        if (body is null
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Message))
            return await Plain(req, HttpStatusCode.BadRequest, "sessionId and message are required");

        var result = await _agent.ChatAsync(
            body.SessionId, body.Message, userId, userEmail, auth0Subject, isAdmin, context.CancellationToken);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { answer = result.Answer, modelUsed = result.ModelUsed });
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

    private record HqChatRequest(
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("message")]   string Message);
}

using System.Net;
using System.Text.Json.Serialization;
using HqAgent.Agents.Contract.Agents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HqAgent.Agents.Contract.Triggers;

/// <summary>
/// HTTP-triggered contract chat endpoint.
/// Called exclusively via the SWA proxy (api/ContractChat.cs) — AuthorizationLevel.Function
/// prevents direct browser access. Identity is pre-validated by the proxy and forwarded
/// via X-User-Id and X-User-Role headers.
/// </summary>
public class ContractChatFunction
{
    private readonly ContractChatAgent _agent;

    public ContractChatFunction(ContractChatAgent agent) => _agent = agent;

    [Function("ContractChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "contract-chat")] HttpRequestData req,
        FunctionContext context)
    {
        var userId  = req.Headers.TryGetValues("X-User-Id",   out var uid)  ? uid.FirstOrDefault()  ?? "" : "";
        var isAdmin = req.Headers.TryGetValues("X-User-Role", out var role) && role.FirstOrDefault() == "admin";

        ChatRequest? body;
        try { body = await System.Text.Json.JsonSerializer.DeserializeAsync<ChatRequest>(req.Body); }
        catch { return await Plain(req, HttpStatusCode.BadRequest, "Invalid JSON body"); }

        if (body is null
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Message))
            return await Plain(req, HttpStatusCode.BadRequest, "sessionId and message are required");

        var result = await _agent.ChatAsync(
            body.CorrelationId, body.SessionId, body.Message, userId, isAdmin,
            context.CancellationToken);

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

    private record ChatRequest(
        [property: JsonPropertyName("correlationId")] string? CorrelationId,
        [property: JsonPropertyName("sessionId")]     string  SessionId,
        [property: JsonPropertyName("message")]       string  Message);
}

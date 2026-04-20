using System.Net;
using System.Text.Json.Serialization;
using HqAgent.Agents.Contract.Agents;
using HqAgent.Shared.Storage;
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
    private readonly ContractChatAgent  _agent;
    private readonly TableStorageService _table;

    public ContractChatFunction(ContractChatAgent agent, TableStorageService table)
    {
        _agent = agent;
        _table = table;
    }

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

        if (body is null || string.IsNullOrWhiteSpace(body.CorrelationId)
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Message))
            return await Plain(req, HttpStatusCode.BadRequest, "correlationId, sessionId and message are required");

        var entity = await _table.GetExtractionAsync(body.CorrelationId);
        if (entity is null)
            return await Plain(req, HttpStatusCode.NotFound, "Contract not found");
        if (!isAdmin && entity.UserId != userId)
            return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");
        if (entity.Status is not ("completed" or "pending_review"))
            return await Plain(req, HttpStatusCode.Conflict, "Contract is not yet processed");

        var result = await _agent.ChatAsync(entity, body.SessionId, body.Message, context.CancellationToken);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { answer = result.Answer, sources = result.Sources, modelUsed = result.ModelUsed, confidence = result.Confidence });
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
        [property: JsonPropertyName("correlationId")] string CorrelationId,
        [property: JsonPropertyName("sessionId")]     string SessionId,
        [property: JsonPropertyName("message")]       string Message);
}

using System.Net;
using System.Text.Json.Serialization;
using HqAgent.Agents.SalesForecast.Agents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HqAgent.Agents.SalesForecast.Triggers;

public class SalesForecastChatFunction
{
    private readonly SalesForecastChatAgent _agent;

    public SalesForecastChatFunction(SalesForecastChatAgent agent) => _agent = agent;

    [Function("SalesForecastChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sales-forecast-chat")] HttpRequestData req,
        FunctionContext context)
    {
        ChatRequest? body;
        try { body = await System.Text.Json.JsonSerializer.DeserializeAsync<ChatRequest>(req.Body); }
        catch { return await Plain(req, HttpStatusCode.BadRequest, "Invalid JSON body"); }

        if (body is null
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Message))
            return await Plain(req, HttpStatusCode.BadRequest, "sessionId and message are required");

        var answer = await _agent.ChatAsync(body.SessionId, body.Message, context.CancellationToken);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { answer });
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
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("history")] IReadOnlyList<ChatTurn>? History = null);

    private record ChatTurn(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}

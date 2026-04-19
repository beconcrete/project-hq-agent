using System.Net;
using System.Text.Json;
using ContractOrchestratorAgent.Services;
using HqAgent.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ContractOrchestratorAgent.Functions;

public class ProcessContract
{
    private readonly ContractProcessor _processor;
    private readonly ILogger<ProcessContract> _logger;

    public ProcessContract(ContractProcessor processor, ILogger<ProcessContract> logger)
    {
        _processor = processor;
        _logger    = logger;
    }

    [Function(nameof(ProcessContract))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "process")] HttpRequestData req,
        FunctionContext context)
    {
        var body = await req.ReadAsStringAsync() ?? string.Empty;

        ContractMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ContractMessage>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialise message");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Invalid message format");
            return bad;
        }

        if (message is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Empty message body");
            return bad;
        }

        try
        {
            await _processor.ProcessAsync(message, context.CancellationToken);
            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Processing failed for correlationId:{CorrelationId}", message.CorrelationId);
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}

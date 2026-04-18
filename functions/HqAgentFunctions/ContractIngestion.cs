using HqAgent.Functions.Models;
using HqAgent.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HqAgent.Functions;

public class ContractIngestion
{
    private readonly ContractWorkflow _workflow;
    private readonly ExtractionTableWriter _tableWriter;
    private readonly ILogger<ContractIngestion> _logger;

    public ContractIngestion(
        ContractWorkflow workflow,
        ExtractionTableWriter tableWriter,
        ILogger<ContractIngestion> logger)
    {
        _workflow = workflow;
        _tableWriter = tableWriter;
        _logger = logger;
    }

    [Function(nameof(ContractIngestion))]
    public async Task Run(
        [QueueTrigger("contract-processing", Connection = "STORAGE_CONNECTION_STRING")] ContractMessage msg,
        FunctionContext context)
    {
        _logger.LogInformation("ContractIngestion triggered for {CorrelationId}", msg.CorrelationId);

        var (extraction, modelUsed) = await _workflow.RunAsync(msg, context.CancellationToken);
        var rawJson = JsonSerializer.Serialize(extraction);
        await _tableWriter.WriteAsync(msg, extraction, rawJson, modelUsed, context.CancellationToken);

        _logger.LogInformation(
            "Contract {CorrelationId} stored — type: {DocumentType}, confidence: {Confidence:F2}, model: {Model}",
            msg.CorrelationId, extraction.DocumentType, extraction.Confidence, modelUsed);
    }
}

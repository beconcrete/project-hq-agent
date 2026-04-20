using ContractOrchestratorAgent.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ContractOrchestratorAgent.Functions;

public class ContractIngestion
{
    private readonly OpenAIContractWorkflow _workflow;
    private readonly TableStorageService _table;
    private readonly ILogger<ContractIngestion> _logger;

    public ContractIngestion(
        OpenAIContractWorkflow workflow,
        TableStorageService table,
        ILogger<ContractIngestion> logger)
    {
        _workflow = workflow;
        _table    = table;
        _logger   = logger;
    }

    [Function(nameof(ContractIngestion))]
    public async Task Run(
        [QueueTrigger("contract-processing", Connection = "STORAGE_CONNECTION_STRING")] ContractMessage msg,
        FunctionContext context)
    {
        _logger.LogInformation("ContractIngestion triggered for {CorrelationId}", msg.CorrelationId);

        ExtractionResult extraction;
        try
        {
            extraction = await _workflow.RunAsync(msg, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContractIngestion failed for {CorrelationId}: {Message}", msg.CorrelationId, ex.Message);
            throw;
        }

        await _table.WriteExtractionAsync(msg.CorrelationId, msg.BlobName, extraction, context.CancellationToken);

        _logger.LogInformation(
            "Contract {CorrelationId} stored — type:{DocumentType} pendingReview:{Pending} model:{Model}",
            msg.CorrelationId, extraction.DocumentType, extraction.PendingReview, extraction.ModelUsed);
    }
}

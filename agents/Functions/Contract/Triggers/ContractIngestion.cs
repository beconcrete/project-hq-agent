using HqAgent.Agents.Contract.Agents;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.Contract.Triggers;

public class ContractIngestion
{
    private readonly ContractOrchestratorAgent _agent;
    private readonly TableStorageService       _table;
    private readonly ILogger<ContractIngestion> _logger;

    public ContractIngestion(
        ContractOrchestratorAgent  agent,
        TableStorageService        table,
        ILogger<ContractIngestion> logger)
    {
        _agent  = agent;
        _table  = table;
        _logger = logger;
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
            extraction = await _agent.RunAsync(msg, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContractIngestion failed for {CorrelationId}: {Message}", msg.CorrelationId, ex.Message);
            await _table.WriteFailedAsync(msg, context.CancellationToken);
            throw;
        }

        await _table.WriteExtractionAsync(msg, extraction, context.CancellationToken);

        _logger.LogInformation(
            "Contract {CorrelationId} stored — type:{DocumentType} pendingReview:{Pending} model:{Model}",
            msg.CorrelationId, extraction.DocumentType, extraction.PendingReview, extraction.ModelUsed);
    }
}

using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.Contract.Triggers;

public class ContractIngestionPoison
{
    private readonly TableStorageService _table;
    private readonly ILogger<ContractIngestionPoison> _logger;

    public ContractIngestionPoison(
        TableStorageService table,
        ILogger<ContractIngestionPoison> logger)
    {
        _table = table;
        _logger = logger;
    }

    [Function(nameof(ContractIngestionPoison))]
    public async Task Run(
        [QueueTrigger("contract-processing-poison", Connection = "STORAGE_CONNECTION_STRING")] ContractMessage msg,
        FunctionContext context)
    {
        _logger.LogError(
            "ContractIngestionPoison triggered for {CorrelationId}; marking upload as failed",
            msg.CorrelationId);

        await _table.WriteFailedAsync(
            msg,
            errorMessage: "Processing exhausted all retries and moved to the poison queue.",
            ct: context.CancellationToken);
    }
}

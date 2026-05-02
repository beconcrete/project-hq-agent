using HqAgent.Agents.Contract.Agents;
using HqAgent.Agents.HQ.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.Contract.Triggers;

public class ContractIngestion
{
    private readonly ContractOrchestratorAgent _agent;
    private readonly TableStorageService       _table;
    private readonly CustomerStorageService    _customers;
    private readonly EmbeddingOrchestrator     _embeddings;
    private readonly ILogger<ContractIngestion> _logger;

    public ContractIngestion(
        ContractOrchestratorAgent  agent,
        TableStorageService        table,
        CustomerStorageService     customers,
        EmbeddingOrchestrator      embeddings,
        ILogger<ContractIngestion> logger)
    {
        _agent      = agent;
        _table      = table;
        _customers  = customers;
        _embeddings = embeddings;
        _logger     = logger;
    }

    [Function(nameof(ContractIngestion))]
    public async Task Run(
        [QueueTrigger("contract-processing", Connection = "STORAGE_CONNECTION_STRING")] ContractMessage msg,
        FunctionContext context)
    {
        _logger.LogInformation("ContractIngestion triggered for {CorrelationId}", msg.CorrelationId);
        var retryCount = GetRetryCount(context);
        var processingLabel = retryCount > 1
            ? $"Retrying extraction (attempt {retryCount})."
            : "Extracting contract.";
        await _table.WriteProcessingAsync(
            msg,
            processingLabel,
            retryCount: retryCount,
            ct: context.CancellationToken);

        ExtractionResult extraction;
        try
        {
            extraction = await _agent.RunAsync(msg, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContractIngestion failed for {CorrelationId}: {Message}", msg.CorrelationId, ex.Message);
            await _table.WriteProcessingAsync(
                msg,
                "Retry scheduled after extraction error.",
                lastError: ex.Message,
                retryCount: retryCount,
                ct: context.CancellationToken);
            throw;
        }

        await _table.WriteExtractionAsync(msg, extraction, context.CancellationToken);
        await LinkCustomersAsync(msg.CorrelationId, extraction, context.CancellationToken);
        // Reload after customer linking so LinkedCustomerNames are included in the embedding
        var contractEntity = await _table.GetExtractionAsync(msg.CorrelationId, context.CancellationToken);
        if (contractEntity is not null)
            await _embeddings.IndexAsync(contractEntity, context.CancellationToken);

        _logger.LogInformation(
            "Contract {CorrelationId} stored — type:{DocumentType} pendingReview:{Pending} model:{Model}",
            msg.CorrelationId, extraction.DocumentType, extraction.PendingReview, extraction.ModelUsed);
    }

    private async Task LinkCustomersAsync(
        string contractId, ExtractionResult extraction, CancellationToken ct)
    {
        try
        {
            var facts        = ContractFactsExtractor.Extract(extraction);
            var counterparties = facts.CounterpartyNames
                .Append(facts.PrimaryCounterparty)
                .Append(facts.CustomerName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var matched = await _customers.MatchByCounterpartiesAsync(counterparties, ct);
            if (matched.Count == 0)
            {
                _logger.LogInformation("No customer matches found for contract {ContractId}", contractId);
                return;
            }

            await _table.UpdateLinkedCustomersAsync(
                contractId,
                matched.Select(c => c.RowKey),
                matched.Select(c => c.Name),
                ct);

            foreach (var customer in matched)
                await _customers.LinkContractAsync(customer.RowKey, contractId, ct);

            _logger.LogInformation(
                "Linked contract {ContractId} to customers: {Names}",
                contractId, string.Join(", ", matched.Select(c => c.Name)));
        }
        catch (Exception ex)
        {
            // Customer linking is best-effort — never fail ingestion over it
            _logger.LogWarning(ex, "Customer linking failed for contract {ContractId}", contractId);
        }
    }

    private static int GetRetryCount(FunctionContext context)
    {
        if (context.BindingContext.BindingData.TryGetValue("DequeueCount", out var dequeueCount) &&
            dequeueCount is not null &&
            int.TryParse(dequeueCount.ToString(), out var parsed) &&
            parsed > 0)
            return parsed;

        return 1;
    }
}

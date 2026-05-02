using HqAgent.Agents.HQ.Services;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HQ.Triggers;

public class EmbeddingIndexerFunction
{
    private readonly EmbeddingOrchestrator   _orchestrator;
    private readonly EmbeddingsStorageService _embeddingsStorage;
    private readonly HRTableStorageService    _hrStorage;
    private readonly CustomerStorageService   _customerStorage;
    private readonly ProjectStorageService    _projectStorage;
    private readonly TableStorageService      _contractStorage;
    private readonly ILogger<EmbeddingIndexerFunction> _logger;

    public EmbeddingIndexerFunction(
        EmbeddingOrchestrator    orchestrator,
        EmbeddingsStorageService embeddingsStorage,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TableStorageService      contractStorage,
        ILogger<EmbeddingIndexerFunction> logger)
    {
        _orchestrator      = orchestrator;
        _embeddingsStorage = embeddingsStorage;
        _hrStorage         = hrStorage;
        _customerStorage   = customerStorage;
        _projectStorage    = projectStorage;
        _contractStorage   = contractStorage;
        _logger            = logger;
    }

    [Function("EmbeddingIndexer")]
    public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var pending = await _embeddingsStorage.ListPendingAsync(ct);
        if (pending.Count == 0)
        {
            _logger.LogInformation("EmbeddingIndexer: no pending entries");
            return;
        }

        _logger.LogInformation("EmbeddingIndexer: re-indexing {Count} pending entries", pending.Count);
        var succeeded = 0;

        foreach (var entry in pending)
        {
            try
            {
                var indexed = await ReindexEntryAsync(entry.PartitionKey, entry.RowKey, ct);
                if (indexed) succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EmbeddingIndexer: failed to re-index {Type}/{Id}", entry.PartitionKey, entry.RowKey);
            }
        }

        _logger.LogInformation("EmbeddingIndexer: {Succeeded}/{Total} succeeded", succeeded, pending.Count);
    }

    private async Task<bool> ReindexEntryAsync(string entityType, string entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case "employee":
            {
                var entity = await _hrStorage.GetEmployeeAsync(entityId, ct);
                if (entity is null) { await _embeddingsStorage.DeleteAsync(entityType, entityId, ct); return false; }
                await _orchestrator.IndexAsync(entity, ct);
                return true;
            }
            case "customer":
            {
                var entity = await _customerStorage.GetCustomerAsync(entityId, ct);
                if (entity is null) { await _embeddingsStorage.DeleteAsync(entityType, entityId, ct); return false; }
                await _orchestrator.IndexAsync(entity, ct);
                return true;
            }
            case "project":
            {
                var entity = await _projectStorage.GetProjectAsync(entityId, ct);
                if (entity is null) { await _embeddingsStorage.DeleteAsync(entityType, entityId, ct); return false; }
                await _orchestrator.IndexAsync(entity, ct);
                return true;
            }
            case "contract":
            {
                var entity = await _contractStorage.GetExtractionAsync(entityId, ct);
                if (entity is null) { await _embeddingsStorage.DeleteAsync(entityType, entityId, ct); return false; }
                await _orchestrator.IndexAsync(entity, ct);
                return true;
            }
            default:
                _logger.LogWarning("EmbeddingIndexer: unknown entity type '{Type}' — skipping", entityType);
                return false;
        }
    }
}

using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class EmbeddingsStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<EmbeddingsStorageService> _logger;

    public EmbeddingsStorageService(TableServiceClient client, ILogger<EmbeddingsStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task UpsertAsync(EmbeddingEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Embeddings);
        await table.CreateIfNotExistsAsync(ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task<EmbeddingEntity?> GetAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Embeddings);
        try
        {
            var response = await table.GetEntityAsync<EmbeddingEntity>(entityType, entityId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<EmbeddingEntity>> ListAllAsync(CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Embeddings);
        await table.CreateIfNotExistsAsync(ct);
        var results = new List<EmbeddingEntity>();
        await foreach (var entity in table.QueryAsync<EmbeddingEntity>(cancellationToken: ct))
            results.Add(entity);
        return results;
    }

    public async Task<List<EmbeddingEntity>> ListPendingAsync(CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Embeddings);
        await table.CreateIfNotExistsAsync(ct);
        var results = new List<EmbeddingEntity>();
        await foreach (var entity in table.QueryAsync<EmbeddingEntity>(
            filter: "Status eq 'pending'", cancellationToken: ct))
            results.Add(entity);
        return results;
    }

    public async Task MarkPendingAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        var existing = await GetAsync(entityType, entityId, ct);
        var entity = existing ?? new EmbeddingEntity { PartitionKey = entityType, RowKey = entityId };
        entity.Status  = "pending";
        entity.Vector  = null;
        await UpsertAsync(entity, ct);
        _logger.LogDebug("Marked {Type}/{Id} as pending for re-index", entityType, entityId);
    }

    public async Task DeleteAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Embeddings);
        try { await table.DeleteEntityAsync(entityType, entityId, cancellationToken: ct); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
    }
}

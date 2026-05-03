using System.Text.Json;
using BeConcrete.EmbeddingCore.Models;
using BeConcrete.EmbeddingCore.Services;
using BeConcrete.EmbeddingCore.Storage;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HQ.Services;

public class EmbeddingOrchestrator
{
    private readonly EmbeddingService _embedding;
    private readonly EmbeddingTextBuilder _textBuilder;
    private readonly EmbeddingsStorageService _storage;
    private readonly EmbeddingCacheService _cache;
    private readonly HRTableStorageService _hrStorage;
    private readonly ILogger<EmbeddingOrchestrator> _logger;

    public EmbeddingOrchestrator(
        EmbeddingService         embedding,
        EmbeddingTextBuilder     textBuilder,
        EmbeddingsStorageService storage,
        EmbeddingCacheService    cache,
        HRTableStorageService    hrStorage,
        ILogger<EmbeddingOrchestrator> logger)
    {
        _embedding   = embedding;
        _textBuilder = textBuilder;
        _storage     = storage;
        _cache       = cache;
        _hrStorage   = hrStorage;
        _logger      = logger;
    }

    public Task IndexAsync(EmployeeEntity entity, CancellationToken ct) =>
        IndexCoreAsync("employee", entity.RowKey, _textBuilder.Build(entity), ct);

    public Task IndexAsync(CustomerEntity entity, CancellationToken ct) =>
        IndexCoreAsync("customer", entity.RowKey, _textBuilder.Build(entity), ct);

    public async Task IndexAsync(ProjectEntity entity, CancellationToken ct)
    {
        var emails = DeserializeStrings(entity.EmployeeEmails);
        var names  = new List<string>();
        foreach (var email in emails)
        {
            var employee = await _hrStorage.GetEmployeeAsync(email, ct);
            if (employee is not null) names.Add(employee.FullName);
        }
        await IndexCoreAsync("project", entity.RowKey, _textBuilder.Build(entity, names), ct);
    }

    public Task IndexAsync(ContractEntity entity, CancellationToken ct) =>
        IndexCoreAsync("contract", entity.RowKey, _textBuilder.Build(entity), ct);

    private async Task IndexCoreAsync(string entityType, string entityId, string text, CancellationToken ct)
    {
        var snippet = EmbeddingTextBuilder.Snippet(text);
        try
        {
            var vector     = await _embedding.GenerateAsync(text, ct);
            var vectorJson = JsonSerializer.Serialize(vector);
            await _storage.UpsertAsync(new EmbeddingEntity
            {
                PartitionKey = entityType,
                RowKey       = entityId,
                Vector       = vectorJson,
                Status       = "ok",
                Snippet      = snippet,
                LastIndexed  = DateTimeOffset.UtcNow,
            }, ct);
            _cache.UpdateEntry(entityType, entityId, vector, snippet);
            _logger.LogDebug("Indexed {Type}/{Id}", entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding failed for {Type}/{Id} — marking pending", entityType, entityId);
            try
            {
                await _storage.UpsertAsync(new EmbeddingEntity
                {
                    PartitionKey = entityType,
                    RowKey       = entityId,
                    Status       = "pending",
                    Snippet      = snippet,
                }, ct);
            }
            catch (Exception storEx)
            {
                _logger.LogError(storEx, "Failed to write pending marker for {Type}/{Id}", entityType, entityId);
            }
        }
    }

    private static string[] DeserializeStrings(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}

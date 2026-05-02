using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HQ.Services;

public record SearchHit(string EntityType, string EntityId, float Score, string Snippet);

public class EmbeddingCacheService
{
    private readonly EmbeddingsStorageService _storage;
    private readonly ILogger<EmbeddingCacheService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Dictionary<(string Type, string Id), CacheEntry> _entries = new();
    private bool _loaded;

    private record CacheEntry(float[] NormalizedVector, string Snippet);

    public EmbeddingCacheService(EmbeddingsStorageService storage, ILogger<EmbeddingCacheService> logger)
    {
        _storage = storage;
        _logger  = logger;
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded) return;
            var all = await _storage.ListAllAsync(ct);
            var loaded = 0;
            foreach (var e in all)
            {
                if (e.Status != "ok" || e.Vector is null) continue;
                try
                {
                    var vector = JsonSerializer.Deserialize<float[]>(e.Vector)!;
                    lock (_entries)
                        _entries[(e.PartitionKey, e.RowKey)] = new CacheEntry(Normalize(vector), e.Snippet);
                    loaded++;
                }
                catch { /* skip corrupt entries */ }
            }
            _loaded = true;
            _logger.LogInformation("EmbeddingCache loaded {Count} entries", loaded);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void UpdateEntry(string entityType, string entityId, float[] vector, string snippet)
    {
        lock (_entries)
            _entries[(entityType, entityId)] = new CacheEntry(Normalize(vector), snippet);
    }

    public void RemoveEntry(string entityType, string entityId)
    {
        lock (_entries)
            _entries.Remove((entityType, entityId));
    }

    public List<SearchHit> Search(float[] queryVector, int limit = 15)
    {
        var normalizedQuery = Normalize(queryVector);
        var results = new List<(float Score, string Type, string Id, string Snippet)>();

        lock (_entries)
        {
            foreach (var kvp in _entries)
            {
                var score = DotProduct(normalizedQuery, kvp.Value.NormalizedVector);
                results.Add((score, kvp.Key.Type, kvp.Key.Id, kvp.Value.Snippet));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .Select(r => new SearchHit(r.Type, r.Id, r.Score, r.Snippet))
            .ToList();
    }

    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm == 0f) return v;
        return v.Select(x => x / norm).ToArray();
    }

    private static float DotProduct(float[] a, float[] b)
    {
        var sum = 0f;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++) sum += a[i] * b[i];
        return sum;
    }
}

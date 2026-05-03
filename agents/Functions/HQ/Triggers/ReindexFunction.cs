using System.Net;
using BeConcrete.EmbeddingCore.Services;
using BeConcrete.EmbeddingCore.Storage;
using HqAgent.Agents.HQ.Services;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HQ.Triggers;

/// <summary>
/// Embeds all entities immediately. The hourly EmbeddingIndexerFunction acts as
/// a safety net for any that fail here. POST /api/management-reindex
/// </summary>
public class ReindexFunction
{
    private readonly EmbeddingOrchestrator    _orchestrator;
    private readonly EmbeddingsStorageService _embeddingsStorage;
    private readonly EmbeddingCacheService    _cache;
    private readonly HRTableStorageService    _hrStorage;
    private readonly CustomerStorageService   _customerStorage;
    private readonly ProjectStorageService    _projectStorage;
    private readonly TableStorageService      _contractStorage;
    private readonly ILogger<ReindexFunction> _logger;

    public ReindexFunction(
        EmbeddingOrchestrator    orchestrator,
        EmbeddingsStorageService embeddingsStorage,
        EmbeddingCacheService    cache,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TableStorageService      contractStorage,
        ILogger<ReindexFunction> logger)
    {
        _orchestrator      = orchestrator;
        _embeddingsStorage = embeddingsStorage;
        _cache             = cache;
        _hrStorage         = hrStorage;
        _customerStorage   = customerStorage;
        _projectStorage    = projectStorage;
        _contractStorage   = contractStorage;
        _logger            = logger;
    }

    [Function("management-reindex")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "management-reindex")] HttpRequestData req,
        CancellationToken ct)
    {
        _logger.LogInformation("ReindexFunction triggered — indexing all entities now");
        var indexed = 0;

        var employees = await _hrStorage.ListEmployeesAsync(includeOffboarded: true, ct: ct);
        foreach (var e in employees)
        {
            await _orchestrator.IndexAsync(e, ct);
            indexed++;
        }

        var customers = await _customerStorage.ListCustomersAsync(includeInactive: true, ct: ct);
        foreach (var c in customers)
        {
            await _orchestrator.IndexAsync(c, ct);
            indexed++;
        }

        var projects = await _projectStorage.ListProjectsAsync(includeClosedProjects: true, ct: ct);
        foreach (var p in projects)
        {
            await _orchestrator.IndexAsync(p, ct);
            indexed++;
        }

        var contracts = await _contractStorage.ListExtractionsAsync(ct: ct);
        foreach (var c in contracts)
        {
            await _orchestrator.IndexAsync(c, ct);
            indexed++;
        }

        _logger.LogInformation("ReindexFunction: {Indexed} entities indexed", indexed);

        // Orphan cleanup — remove embeddings for entities that no longer exist
        var pruned = await PruneOrphansAsync(employees, customers, projects, contracts, ct);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { indexed, pruned });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private async Task<int> PruneOrphansAsync(
        IEnumerable<HqAgent.Shared.Models.EmployeeEntity> employees,
        IEnumerable<HqAgent.Shared.Models.CustomerEntity> customers,
        IEnumerable<HqAgent.Shared.Models.ProjectEntity>  projects,
        IEnumerable<HqAgent.Shared.Models.ContractEntity> contracts,
        CancellationToken ct)
    {
        var knownIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["employee"] = employees.Select(e => e.RowKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["customer"] = customers.Select(c => c.RowKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["project"]  = projects.Select(p => p.RowKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["contract"] = contracts.Select(c => c.RowKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

        var allEmbeddings = await _embeddingsStorage.ListAllAsync(ct);
        var pruned = 0;

        foreach (var emb in allEmbeddings)
        {
            if (knownIds.TryGetValue(emb.PartitionKey, out var ids) && ids.Contains(emb.RowKey))
                continue;

            await _embeddingsStorage.DeleteAsync(emb.PartitionKey, emb.RowKey, ct);
            _cache.RemoveEntry(emb.PartitionKey, emb.RowKey);
            pruned++;
            _logger.LogInformation("ReindexFunction: pruned orphan embedding {Type}/{Id}", emb.PartitionKey, emb.RowKey);
        }

        if (pruned > 0)
            _logger.LogInformation("ReindexFunction: pruned {Pruned} orphan embeddings total", pruned);

        return pruned;
    }
}

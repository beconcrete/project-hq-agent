using System.Net;
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
    private readonly EmbeddingOrchestrator   _orchestrator;
    private readonly HRTableStorageService   _hrStorage;
    private readonly CustomerStorageService  _customerStorage;
    private readonly ProjectStorageService   _projectStorage;
    private readonly TableStorageService     _contractStorage;
    private readonly ILogger<ReindexFunction> _logger;

    public ReindexFunction(
        EmbeddingOrchestrator    orchestrator,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TableStorageService      contractStorage,
        ILogger<ReindexFunction> logger)
    {
        _orchestrator    = orchestrator;
        _hrStorage       = hrStorage;
        _customerStorage = customerStorage;
        _projectStorage  = projectStorage;
        _contractStorage = contractStorage;
        _logger          = logger;
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

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { indexed });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }
}

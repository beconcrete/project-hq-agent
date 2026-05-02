using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HQ.Triggers;

/// <summary>
/// Marks all entities as pending for re-embedding. The EmbeddingIndexerFunction
/// processes them on its hourly schedule. Requires the Azure Functions admin key.
/// POST /api/management-reindex
/// </summary>
public class ReindexFunction
{
    private readonly EmbeddingsStorageService _embeddingsStorage;
    private readonly HRTableStorageService    _hrStorage;
    private readonly CustomerStorageService   _customerStorage;
    private readonly ProjectStorageService    _projectStorage;
    private readonly TableStorageService      _contractStorage;
    private readonly ILogger<ReindexFunction> _logger;

    public ReindexFunction(
        EmbeddingsStorageService embeddingsStorage,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TableStorageService      contractStorage,
        ILogger<ReindexFunction> logger)
    {
        _embeddingsStorage = embeddingsStorage;
        _hrStorage         = hrStorage;
        _customerStorage   = customerStorage;
        _projectStorage    = projectStorage;
        _contractStorage   = contractStorage;
        _logger            = logger;
    }

    [Function("management-reindex")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Admin, "post", Route = "management-reindex")] HttpRequestData req,
        CancellationToken ct)
    {
        _logger.LogInformation("ReindexFunction triggered — marking all entities pending");
        var queued = 0;

        var employees = await _hrStorage.ListEmployeesAsync(includeOffboarded: true, ct: ct);
        foreach (var e in employees)
        {
            await _embeddingsStorage.MarkPendingAsync("employee", e.RowKey, ct);
            queued++;
        }

        var customers = await _customerStorage.ListCustomersAsync(includeInactive: true, ct: ct);
        foreach (var c in customers)
        {
            await _embeddingsStorage.MarkPendingAsync("customer", c.RowKey, ct);
            queued++;
        }

        var projects = await _projectStorage.ListProjectsAsync(includeClosedProjects: true, ct: ct);
        foreach (var p in projects)
        {
            await _embeddingsStorage.MarkPendingAsync("project", p.RowKey, ct);
            queued++;
        }

        var contracts = await _contractStorage.ListExtractionsAsync(ct: ct);
        foreach (var c in contracts)
        {
            await _embeddingsStorage.MarkPendingAsync("contract", c.RowKey, ct);
            queued++;
        }

        _logger.LogInformation("ReindexFunction: {Queued} entities marked pending", queued);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { queued });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }
}

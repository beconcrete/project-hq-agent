using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class ProjectStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<ProjectStorageService> _logger;

    public ProjectStorageService(TableServiceClient client, ILogger<ProjectStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<ProjectEntity>> ListProjectsAsync(bool includeClosedProjects = false, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Projects);
        await table.CreateIfNotExistsAsync(ct);

        var results = new List<ProjectEntity>();
        await foreach (var entity in table.QueryAsync<ProjectEntity>(
            filter: "PartitionKey eq 'projects'", cancellationToken: ct))
        {
            if (!includeClosedProjects && entity.Status == "closed")
                continue;
            results.Add(entity);
        }

        return results.OrderBy(p => p.Name).ToList();
    }

    public async Task<ProjectEntity?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Projects);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<ProjectEntity>("projects", projectId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<ProjectEntity>> ListByCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var all = await ListProjectsAsync(includeClosedProjects: true, ct: ct);
        return all.Where(p => p.CustomerId == customerId).ToList();
    }

    public async Task<List<ProjectEntity>> ListByEmployeeAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.ToLowerInvariant();
        var all = await ListProjectsAsync(includeClosedProjects: true, ct: ct);
        return all.Where(p =>
        {
            try
            {
                var emails = JsonSerializer.Deserialize<string[]>(p.EmployeeEmails) ?? [];
                return emails.Any(e => e.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }).ToList();
    }

    public async Task<ProjectEntity?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var projects = await ListProjectsAsync(includeClosedProjects: true, ct: ct);
        return projects.FirstOrDefault(p =>
            p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task WriteProjectAsync(ProjectEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Projects);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "projects";
        if (string.IsNullOrWhiteSpace(entity.RowKey))
            entity.RowKey = Guid.NewGuid().ToString();

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Wrote project {ProjectId} ({Name})", entity.RowKey, entity.Name);
    }
}

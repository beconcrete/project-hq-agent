using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class TimereportStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<TimereportStorageService> _logger;

    public TimereportStorageService(TableServiceClient client, ILogger<TimereportStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<TimereportEntity> LogTimeAsync(
        string employeeId,
        string workEmail,
        string projectId,
        string projectName,
        string customerId,
        string customerName,
        double hours,
        string note,
        DateOnly date,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Timereports);
        await table.CreateIfNotExistsAsync(ct);

        var ticks  = DateTime.UtcNow.Ticks;
        var entity = new TimereportEntity
        {
            PartitionKey = employeeId,
            RowKey       = $"{date:yyyyMMdd}_{projectId}_{ticks:D20}",
            EmployeeId   = employeeId,
            WorkEmail    = workEmail,
            ProjectId    = projectId,
            ProjectName  = projectName,
            CustomerId   = customerId,
            CustomerName = customerName,
            Hours        = hours,
            Note         = note,
            ReportedAt   = DateTime.UtcNow,
            ReportDate   = date.ToString("yyyy-MM-dd"),
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation(
            "Logged {Hours}h for employee {EmployeeId} ({WorkEmail}) on project {ProjectId} date {Date}",
            hours, employeeId, workEmail, projectId, date);

        return entity;
    }

    public async Task<bool> DeleteAsync(
        string employeeId,
        string rowKey,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Timereports);
        try
        {
            await table.DeleteEntityAsync(employeeId, rowKey, ETag.All, ct);
            _logger.LogInformation("Deleted timereport {RowKey} for employee {EmployeeId}", rowKey, employeeId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> UpdateNoteAsync(
        string employeeId,
        string rowKey,
        string note,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Timereports);

        try
        {
            var entity = (await table.GetEntityAsync<TimereportEntity>(
                employeeId, rowKey, cancellationToken: ct)).Value;
            entity.Note = note;
            await table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace, ct);
            _logger.LogInformation("Updated note on timereport {RowKey}", rowKey);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    // Returns all timereport entries matching the given filters.
    // Date range filters on the yyyyMMdd prefix of RowKey when employeeId is specified.
    public async Task<List<TimereportEntity>> QueryAsync(
        string?           employeeId = null,
        string?           projectId  = null,
        string?           customerId = null,
        DateOnly?         from       = null,
        DateOnly?         to         = null,
        CancellationToken ct         = default)
    {
        var table = _client.GetTableClient(TableNames.Timereports);
        await table.CreateIfNotExistsAsync(ct);

        var results = new List<TimereportEntity>();

        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var filter = $"PartitionKey eq '{employeeId}'";
            if (from.HasValue)
                filter += $" and RowKey ge '{from.Value:yyyyMMdd}'";
            if (to.HasValue)
                filter += $" and RowKey le '{to.Value:yyyyMMdd}_z'";

            await foreach (var e in table.QueryAsync<TimereportEntity>(filter: filter, cancellationToken: ct))
                results.Add(e);
        }
        else
        {
            await foreach (var e in table.QueryAsync<TimereportEntity>(cancellationToken: ct))
                results.Add(e);

            if (from.HasValue)
                results = results.Where(e => DateOnly.Parse(e.ReportDate) >= from.Value).ToList();
            if (to.HasValue)
                results = results.Where(e => DateOnly.Parse(e.ReportDate) <= to.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(projectId))
            results = results.Where(e => e.ProjectId == projectId).ToList();
        if (!string.IsNullOrWhiteSpace(customerId))
            results = results.Where(e => e.CustomerId == customerId).ToList();

        return results.OrderBy(e => e.ReportDate).ThenBy(e => e.RowKey).ToList();
    }
}

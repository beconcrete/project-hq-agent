using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class HRTableStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<HRTableStorageService> _logger;
    public HRTableStorageService(TableServiceClient client, ILogger<HRTableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<EmployeeEntity>> ListEmployeesAsync(bool includeOffboarded = false, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        await table.CreateIfNotExistsAsync(ct);

        var results = new List<EmployeeEntity>();
        await foreach (var entity in table.QueryAsync<EmployeeEntity>(
            filter: "PartitionKey eq 'employees'", cancellationToken: ct))
        {
            if (!includeOffboarded && entity.Status == "offboarded")
                continue;
            results.Add(entity);
        }

        return results.OrderBy(e => e.FullName).ToList();
    }

    public async Task<EmployeeEntity?> GetEmployeeAsync(string email, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<EmployeeEntity>(
                "employees", email.ToLowerInvariant(), cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task WriteEmployeeAsync(EmployeeEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "employees";
        entity.RowKey       = entity.Email.ToLowerInvariant();

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Wrote employee {Email} ({FullName}), status={Status}", entity.RowKey, entity.FullName, entity.Status);
    }

    // Always reads fresh — never cached. Callers must not cache the return value.
    public async Task<HRConfigEntity> GetHRConfigAsync(CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.HRConfig);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<HRConfigEntity>("hrconfig", "default", cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var defaults = new HRConfigEntity();
            await table.UpsertEntityAsync(defaults, TableUpdateMode.Replace, ct);
            _logger.LogInformation("HRConfig row not found — seeded defaults (StandardHoursDeduction={StandardHoursDeduction}, UtilizationTarget={UtilizationTarget})", defaults.StandardHoursDeduction, defaults.UtilizationTarget);
            return defaults;
        }
    }

    public async Task WriteHRConfigAsync(HRConfigEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.HRConfig);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "hrconfig";
        entity.RowKey = "default";

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Updated HRConfig: StandardHoursDeduction={StandardHoursDeduction}, UtilizationTarget={UtilizationTarget}", entity.StandardHoursDeduction, entity.UtilizationTarget);
    }
}

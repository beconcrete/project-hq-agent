using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class HRTableStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<HRTableStorageService> _logger;
    private const string EmployeesTable = "Employees";
    private const string HRConfigTable = "HRConfig";

    public HRTableStorageService(TableServiceClient client, ILogger<HRTableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<EmployeeEntity>> ListEmployeesAsync(bool includeOffboarded = false, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(EmployeesTable);
        await table.CreateIfNotExistsAsync(ct);

        var results = new List<EmployeeEntity>();
        await foreach (var entity in table.QueryAsync<EmployeeEntity>(cancellationToken: ct))
        {
            if (!includeOffboarded && entity.Status == "offboarded")
                continue;
            results.Add(entity);
        }

        return results.OrderBy(e => e.FullName).ToList();
    }

    public async Task<EmployeeEntity?> GetEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(EmployeesTable);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<EmployeeEntity>("employees", employeeId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task WriteEmployeeAsync(EmployeeEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(EmployeesTable);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "employees";
        if (string.IsNullOrWhiteSpace(entity.RowKey))
            entity.RowKey = Guid.NewGuid().ToString();

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Wrote employee {EmployeeId} ({FullName}), status={Status}", entity.RowKey, entity.FullName, entity.Status);
    }

    // Always reads fresh — never cached. Callers must not cache the return value.
    public async Task<HRConfigEntity> GetHRConfigAsync(CancellationToken ct = default)
    {
        var table = _client.GetTableClient(HRConfigTable);
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
            _logger.LogInformation("HRConfig row not found — seeded defaults (BonusThreshold={BonusThreshold}, UtilizationTarget={UtilizationTarget})", defaults.BonusThreshold, defaults.UtilizationTarget);
            return defaults;
        }
    }

    public async Task WriteHRConfigAsync(HRConfigEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(HRConfigTable);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "hrconfig";
        entity.RowKey = "default";

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Updated HRConfig: BonusThreshold={BonusThreshold}, UtilizationTarget={UtilizationTarget}", entity.BonusThreshold, entity.UtilizationTarget);
    }
}

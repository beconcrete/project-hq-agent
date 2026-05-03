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

    // Lookup by employeeId (GUID RowKey).
    public async Task<EmployeeEntity?> GetEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<EmployeeEntity>(
                "employees", employeeId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // Find by Auth0 subject (most reliable), then by loginEmail, then by workEmail.
    // Used on every chat request to identify the signed-in user's employee record.
    public async Task<EmployeeEntity?> FindByAuthAsync(
        string? auth0Subject,
        string? loginEmail,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(auth0Subject) && string.IsNullOrEmpty(loginEmail))
            return null;

        var all = await ListEmployeesAsync(includeOffboarded: false, ct);

        if (!string.IsNullOrEmpty(auth0Subject))
        {
            var match = all.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.Auth0Subject) &&
                e.Auth0Subject.Equals(auth0Subject, StringComparison.Ordinal));
            if (match is not null) return match;
        }

        if (!string.IsNullOrEmpty(loginEmail))
        {
            var lower = loginEmail.ToLowerInvariant();
            var match = all.FirstOrDefault(e =>
                (!string.IsNullOrEmpty(e.LoginEmail)  && e.LoginEmail.Equals(lower, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.WorkEmail)   && e.WorkEmail.Equals(lower, StringComparison.OrdinalIgnoreCase)));
            if (match is not null) return match;
        }

        return null;
    }

    public async Task WriteEmployeeAsync(EmployeeEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "employees";
        // RowKey must be a GUID set by the caller — never derived from email.
        if (string.IsNullOrWhiteSpace(entity.RowKey))
            entity.RowKey = Guid.NewGuid().ToString();

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Wrote employee {EmployeeId} ({FullName}), status={Status}", entity.RowKey, entity.FullName, entity.Status);
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

    public async Task DeleteEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Employees);
        try { await table.DeleteEntityAsync("employees", employeeId, cancellationToken: ct); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }
        _logger.LogInformation("Deleted employee {EmployeeId}", employeeId);
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

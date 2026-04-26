using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class ForecastTableStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<ForecastTableStorageService> _logger;

    public ForecastTableStorageService(
        TableServiceClient client,
        ILogger<ForecastTableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<WorkingHoursEntity?> GetWorkingHoursAsync(int month, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.WorkingHours);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<WorkingHoursEntity>(
                "2026",
                month.ToString("D2"),
                cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Working hours row not found for month {Month}", month);
            return null;
        }
    }

    public async Task<SeniorityRateEntity?> GetSeniorityRateAsync(string role, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.SeniorityRates);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<SeniorityRateEntity>(
                "default",
                role,
                cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Seniority rate row not found for role {Role}", role);
            return null;
        }
    }

    public async Task SeedAsync(
        IEnumerable<WorkingHoursEntity> workingHours,
        IEnumerable<SeniorityRateEntity> seniorityRates,
        CancellationToken ct = default)
    {
        var workingHoursTable = _client.GetTableClient(TableNames.WorkingHours);
        var seniorityRatesTable = _client.GetTableClient(TableNames.SeniorityRates);

        await workingHoursTable.CreateIfNotExistsAsync(ct);
        await seniorityRatesTable.CreateIfNotExistsAsync(ct);

        foreach (var entity in workingHours)
            await workingHoursTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        foreach (var entity in seniorityRates)
            await seniorityRatesTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }
}

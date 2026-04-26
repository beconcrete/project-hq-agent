using System.Net;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HqAgent.Agents.SalesForecast.Triggers;

public class SeedForecastData
{
    private readonly ForecastTableStorageService _storage;

    public SeedForecastData(ForecastTableStorageService storage) => _storage = storage;

    [Function("SeedForecastData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed-forecast-data")] HttpRequestData req,
        FunctionContext context)
    {
        var isAdmin = req.Headers.TryGetValues("X-User-Role", out var roles) &&
            string.Equals(roles.FirstOrDefault(), "admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin)
            return await Plain(req, HttpStatusCode.Forbidden, "Admin role is required");

        await _storage.SeedAsync(GetWorkingHoursSeedData(), GetSeniorityRateSeedData(), context.CancellationToken);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new
        {
            message = "Forecast reference data seeded",
            workingHoursRows = 12,
            seniorityRateRows = 3,
        });
        return res;
    }

    private static IEnumerable<WorkingHoursEntity> GetWorkingHoursSeedData() =>
    [
        new() { PartitionKey = "2026", RowKey = "01", WorkingDays = 21, VacationDays = 0, AvailableDays = 21, AvailableHours = 168 },
        new() { PartitionKey = "2026", RowKey = "02", WorkingDays = 20, VacationDays = 0, AvailableDays = 20, AvailableHours = 160 },
        new() { PartitionKey = "2026", RowKey = "03", WorkingDays = 22, VacationDays = 0, AvailableDays = 22, AvailableHours = 176 },
        new() { PartitionKey = "2026", RowKey = "04", WorkingDays = 20, VacationDays = 0, AvailableDays = 20, AvailableHours = 160 },
        new() { PartitionKey = "2026", RowKey = "05", WorkingDays = 20, VacationDays = 0, AvailableDays = 20, AvailableHours = 160 },
        new() { PartitionKey = "2026", RowKey = "06", WorkingDays = 19, VacationDays = 5, AvailableDays = 14, AvailableHours = 112 },
        new() { PartitionKey = "2026", RowKey = "07", WorkingDays = 23, VacationDays = 15, AvailableDays = 8, AvailableHours = 64 },
        new() { PartitionKey = "2026", RowKey = "08", WorkingDays = 22, VacationDays = 5, AvailableDays = 17, AvailableHours = 136 },
        new() { PartitionKey = "2026", RowKey = "09", WorkingDays = 22, VacationDays = 0, AvailableDays = 22, AvailableHours = 176 },
        new() { PartitionKey = "2026", RowKey = "10", WorkingDays = 23, VacationDays = 0, AvailableDays = 23, AvailableHours = 184 },
        new() { PartitionKey = "2026", RowKey = "11", WorkingDays = 20, VacationDays = 0, AvailableDays = 20, AvailableHours = 160 },
        new() { PartitionKey = "2026", RowKey = "12", WorkingDays = 19, VacationDays = 5, AvailableDays = 14, AvailableHours = 112 },
    ];

    private static IEnumerable<SeniorityRateEntity> GetSeniorityRateSeedData() =>
    [
        new() { PartitionKey = "default", RowKey = "Senior", HourlyRateSEK = 1350m, Utilization = 0.85, MixPercent = 0.30 },
        new() { PartitionKey = "default", RowKey = "Medior", HourlyRateSEK = 1100m, Utilization = 0.85, MixPercent = 0.50 },
        new() { PartitionKey = "default", RowKey = "Junior", HourlyRateSEK = 900m, Utilization = 0.85, MixPercent = 0.20 },
    ];

    private static async Task<HttpResponseData> Plain(
        HttpRequestData req,
        HttpStatusCode status,
        string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }
}

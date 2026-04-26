using HqAgent.Shared.Models;

namespace HqAgent.Agents.SalesForecast.Services;

public interface ISalesForecastIntelligence
{
    Task<MonthlyForecastSummary> GetMonthlyForecastAsync(int year, int month, CancellationToken ct);
    Task<ForecastResult?> GetConsultantForecastAsync(string consultantName, int year, int month, CancellationToken ct);
}

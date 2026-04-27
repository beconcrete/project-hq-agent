using HqAgent.Shared.Models;

namespace HqAgent.Agents.SalesForecast.Services;

public enum SalesForecastResponseMode
{
    MonthlyOverview,
    MonthlyBreakdown,
    BookedVsEstimated,
    ConsultantDetail,
}

public enum SalesForecastLanguage
{
    English,
    Swedish,
}

public sealed record SalesForecastPeriod(int Year, int Month);

public sealed record SalesForecastStructuredRequest(
    SalesForecastResponseMode Mode,
    SalesForecastLanguage Language,
    IReadOnlyList<SalesForecastPeriod> Periods,
    string? ConsultantName = null);

public sealed record SalesForecastStructuredResponse(
    SalesForecastResponseMode Mode,
    SalesForecastLanguage Language,
    string Text);

public sealed record MonthlyOverviewData(
    MonthlyForecastSummary Summary,
    decimal TotalPlannedRevenue,
    decimal TotalBookedRevenue,
    decimal TotalUnbookedRevenue,
    double TotalPlannedHours,
    int BookedHeadcount,
    int UnbookedHeadcount,
    decimal AveragePlannedHourlyRate);

public sealed record ConsultantDetailData(
    string ConsultantName,
    MonthlyForecastSummary Summary,
    ForecastResult Forecast);

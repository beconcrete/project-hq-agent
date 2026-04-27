using HqAgent.Shared.Models;

namespace HqAgent.Agents.SalesForecast.Services;

public static class SalesForecastResponseValidator
{
    public static MonthlyOverviewData ValidateMonthlySummary(MonthlyForecastSummary summary)
    {
        var plannedRevenue = Decimal.Round(summary.TotalBookedRevenue + summary.TotalUnbookedEstimate, 2);
        if (summary.TotalPlannedRevenue != plannedRevenue)
        {
            throw new InvalidOperationException(
                $"Forecast summary is inconsistent for {summary.Year}-{summary.Month:D2}: planned revenue {summary.TotalPlannedRevenue} does not equal booked + unbooked ({plannedRevenue}).");
        }

        var plannedHours = summary.TotalBookedHours + summary.TotalUnbookedHours;
        if (Math.Abs(summary.TotalPlannedHours - plannedHours) > 0.01d)
        {
            throw new InvalidOperationException(
                $"Forecast summary is inconsistent for {summary.Year}-{summary.Month:D2}: planned hours {summary.TotalPlannedHours} do not equal booked + unbooked ({plannedHours}).");
        }

        if (summary.UnbookedHeadcount > 0 &&
            summary.TotalUnbookedHours > 0d &&
            summary.TotalUnbookedEstimate <= 0m)
        {
            throw new InvalidOperationException(
                $"Forecast summary is inconsistent for {summary.Year}-{summary.Month:D2}: unbooked consultants have hours but zero unbooked estimate.");
        }

        var bookedRate = summary.TotalBookedHours > 0d
            ? Decimal.Round(summary.TotalBookedRevenue / (decimal)summary.TotalBookedHours, 2)
            : 0m;
        var plannedRate = summary.TotalPlannedHours > 0d
            ? Decimal.Round(summary.TotalPlannedRevenue / (decimal)summary.TotalPlannedHours, 2)
            : 0m;

        foreach (var consultant in summary.Consultants.Where(c => c.Status == ForecastStatus.Booked))
        {
            if (!string.Equals(consultant.HourlyRateBasis, "contract", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Forecast summary is inconsistent for {summary.Year}-{summary.Month:D2}: booked consultant {consultant.Name} does not use contract hourly rate basis.");
            }
        }

        foreach (var consultant in summary.Consultants.Where(c => c.Status == ForecastStatus.Unbooked))
        {
            if (!string.Equals(consultant.HourlyRateBasis, "seniority-benchmark", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Forecast summary is inconsistent for {summary.Year}-{summary.Month:D2}: unbooked consultant {consultant.Name} does not use benchmark hourly rate basis.");
            }
        }

        return new MonthlyOverviewData(
            summary,
            summary.TotalPlannedRevenue,
            summary.TotalBookedRevenue,
            summary.TotalUnbookedEstimate,
            summary.TotalPlannedHours,
            summary.BookedHeadcount,
            summary.UnbookedHeadcount,
            plannedRate == 0m ? bookedRate : plannedRate);
    }

    public static ConsultantDetailData ValidateConsultantDetail(
        string consultantName,
        MonthlyForecastSummary summary,
        ForecastResult forecast)
    {
        _ = ValidateMonthlySummary(summary);

        if (!string.Equals(forecast.Name, consultantName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Consultant detail mismatch: expected {consultantName}, got {forecast.Name}.");
        }

        return new ConsultantDetailData(consultantName, summary, forecast);
    }
}

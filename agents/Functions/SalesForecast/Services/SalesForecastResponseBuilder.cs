using System.Globalization;

namespace HqAgent.Agents.SalesForecast.Services;

public static class SalesForecastResponseBuilder
{
    public static SalesForecastStructuredResponse BuildMonthlyOverview(
        SalesForecastLanguage language,
        MonthlyOverviewData data)
    {
        var monthLabel = FormatMonthYear(data.Summary.Year, data.Summary.Month, language);
        var text = language switch
        {
            SalesForecastLanguage.Swedish => string.Join(Environment.NewLine, [
                $"{monthLabel}:",
                $"- Planerad intäkt: {FormatSek(data.TotalPlannedRevenue, language)}",
                $"- Bokad intäkt: {FormatSek(data.TotalBookedRevenue, language)}",
                $"- Uppskattad obokad intäkt: {FormatSek(data.TotalUnbookedRevenue, language)}",
                $"- Bokade konsulter: {data.BookedHeadcount}",
                $"- Obokade konsulter: {data.UnbookedHeadcount}",
                $"- Planerade timmar: {FormatHours(data.TotalPlannedHours)}",
                $"- Genomsnittligt timpris: {FormatSek(data.AveragePlannedHourlyRate, language)}",
                "",
                "Jag kan bryta ned detta per konsult eller förklara vad som är bokat respektive uppskattat."
            ]),
            _ => string.Join(Environment.NewLine, [
                $"{monthLabel}:",
                $"- Planned revenue: {FormatSek(data.TotalPlannedRevenue, language)}",
                $"- Booked revenue: {FormatSek(data.TotalBookedRevenue, language)}",
                $"- Unbooked estimate: {FormatSek(data.TotalUnbookedRevenue, language)}",
                $"- Booked consultants: {data.BookedHeadcount}",
                $"- Unbooked consultants: {data.UnbookedHeadcount}",
                $"- Planned hours: {FormatHours(data.TotalPlannedHours)}",
                $"- Average hourly rate: {FormatSek(data.AveragePlannedHourlyRate, language)}",
                "",
                "I can break this down by consultant or explain what is booked versus estimated."
            ]),
        };

        return new SalesForecastStructuredResponse(SalesForecastResponseMode.MonthlyOverview, language, text);
    }

    public static SalesForecastStructuredResponse BuildMonthlyBreakdown(
        SalesForecastLanguage language,
        IReadOnlyList<MonthlyOverviewData> months)
    {
        var lines = new List<string>();
        if (language == SalesForecastLanguage.Swedish)
        {
            lines.Add("Så här ser återstoden av perioden ut månad för månad:");
            lines.Add(string.Empty);
            foreach (var month in months)
            {
                lines.Add($"{FormatMonthYear(month.Summary.Year, month.Summary.Month, language)}:");
                lines.Add($"- Planerad intäkt: {FormatSek(month.TotalPlannedRevenue, language)}");
                lines.Add($"- Bokad intäkt: {FormatSek(month.TotalBookedRevenue, language)}");
                lines.Add($"- Uppskattad obokad intäkt: {FormatSek(month.TotalUnbookedRevenue, language)}");
                lines.Add($"- Bokade konsulter: {month.BookedHeadcount}");
                lines.Add($"- Obokade konsulter: {month.UnbookedHeadcount}");
                lines.Add($"- Planerade timmar: {FormatHours(month.TotalPlannedHours)}");
                lines.Add($"- Genomsnittligt timpris: {FormatSek(month.AveragePlannedHourlyRate, language)}");
                lines.Add(string.Empty);
            }
            lines.Add("Jag kan gå vidare med en viss månad, förklara bokat mot uppskattat, eller bryta ned det per konsult.");
        }
        else
        {
            lines.Add("Here is the month-by-month forecast for the remaining period:");
            lines.Add(string.Empty);
            foreach (var month in months)
            {
                lines.Add($"{FormatMonthYear(month.Summary.Year, month.Summary.Month, language)}:");
                lines.Add($"- Planned revenue: {FormatSek(month.TotalPlannedRevenue, language)}");
                lines.Add($"- Booked revenue: {FormatSek(month.TotalBookedRevenue, language)}");
                lines.Add($"- Unbooked estimate: {FormatSek(month.TotalUnbookedRevenue, language)}");
                lines.Add($"- Booked consultants: {month.BookedHeadcount}");
                lines.Add($"- Unbooked consultants: {month.UnbookedHeadcount}");
                lines.Add($"- Planned hours: {FormatHours(month.TotalPlannedHours)}");
                lines.Add($"- Average hourly rate: {FormatSek(month.AveragePlannedHourlyRate, language)}");
                lines.Add(string.Empty);
            }
            lines.Add("I can drill into a specific month, explain booked versus estimated, or break it down by consultant.");
        }

        return new SalesForecastStructuredResponse(SalesForecastResponseMode.MonthlyBreakdown, language, string.Join(Environment.NewLine, lines));
    }

    public static SalesForecastStructuredResponse BuildBookedVsEstimated(
        SalesForecastLanguage language,
        MonthlyOverviewData data)
    {
        var monthLabel = FormatMonthYear(data.Summary.Year, data.Summary.Month, language);
        var text = language switch
        {
            SalesForecastLanguage.Swedish => string.Join(Environment.NewLine, [
                $"{monthLabel}:",
                $"- Planerad intäkt: {FormatSek(data.TotalPlannedRevenue, language)}",
                $"- Bokad intäkt: {FormatSek(data.TotalBookedRevenue, language)}",
                $"- Uppskattad obokad intäkt: {FormatSek(data.TotalUnbookedRevenue, language)}",
                $"- Bokade timmar: {FormatHours(data.Summary.TotalBookedHours)}",
                $"- Uppskattade obokade timmar: {FormatHours(data.Summary.TotalUnbookedHours)}",
                "",
                "Den bokade delen kommer från aktiva konsultkontrakt i månaden. Den uppskattade delen kommer från konsulter utan aktivt kontrakt, där prognosen använder benchmark-timpris och aktuell beläggningsregel."
            ]),
            _ => string.Join(Environment.NewLine, [
                $"{monthLabel}:",
                $"- Planned revenue: {FormatSek(data.TotalPlannedRevenue, language)}",
                $"- Booked revenue: {FormatSek(data.TotalBookedRevenue, language)}",
                $"- Unbooked estimate: {FormatSek(data.TotalUnbookedRevenue, language)}",
                $"- Booked hours: {FormatHours(data.Summary.TotalBookedHours)}",
                $"- Estimated unbooked hours: {FormatHours(data.Summary.TotalUnbookedHours)}",
                "",
                "The booked part comes from active consultant contracts in the month. The estimated part comes from consultants without an active contract, using the benchmark hourly rate and the current utilization rule."
            ]),
        };

        return new SalesForecastStructuredResponse(SalesForecastResponseMode.BookedVsEstimated, language, text);
    }

    public static SalesForecastStructuredResponse BuildConsultantDetail(
        SalesForecastLanguage language,
        ConsultantDetailData data)
    {
        var monthLabel = FormatMonthYear(data.Summary.Year, data.Summary.Month, language);
        var forecast = data.Forecast;
        var lines = new List<string>();
        if (language == SalesForecastLanguage.Swedish)
        {
            lines.Add($"{data.ConsultantName} i {monthLabel}:");
            lines.Add($"- Prognostiserad intäkt: {FormatSek(forecast.EstimatedRevenueSEK, language)}");
            lines.Add($"- Prognostiserade timmar: {FormatHours(forecast.BillableHours)}");
            lines.Add($"- Timpris: {FormatSek(forecast.HourlyRate, language)}");
            lines.Add($"- Beläggning: {FormatPercent(forecast.UtilizationApplied)}");
            lines.Add(string.Empty);
            lines.Add("Så här räknas det:");
            lines.Add($"- {forecast.CalculationDetails}");

            if (forecast.Status == HqAgent.Shared.Models.ForecastStatus.Booked &&
                !string.IsNullOrWhiteSpace(forecast.ContractStartDate) &&
                !string.IsNullOrWhiteSpace(forecast.ContractEndDate))
            {
                lines.Add($"- Aktivt kontrakt i månaden: {forecast.ContractStartDate} till {forecast.ContractEndDate}.");
            }
            else
            {
                lines.Add("- Ingen aktiv kontraktsrad i månaden, så prognosen använder benchmark-timpris och regelstyrd beläggning.");
            }
        }
        else
        {
            lines.Add($"{data.ConsultantName} in {monthLabel}:");
            lines.Add($"- Forecast revenue: {FormatSek(forecast.EstimatedRevenueSEK, language)}");
            lines.Add($"- Forecast hours: {FormatHours(forecast.BillableHours)}");
            lines.Add($"- Hourly rate: {FormatSek(forecast.HourlyRate, language)}");
            lines.Add($"- Utilization: {FormatPercent(forecast.UtilizationApplied)}");
            lines.Add(string.Empty);
            lines.Add("This is how it is calculated:");
            lines.Add($"- {forecast.CalculationDetails}");

            if (forecast.Status == HqAgent.Shared.Models.ForecastStatus.Booked &&
                !string.IsNullOrWhiteSpace(forecast.ContractStartDate) &&
                !string.IsNullOrWhiteSpace(forecast.ContractEndDate))
            {
                lines.Add($"- Active contract in the month: {forecast.ContractStartDate} to {forecast.ContractEndDate}.");
            }
            else
            {
                lines.Add("- There is no active contract row in the month, so the forecast uses benchmark hourly rate and rule-based utilization.");
            }
        }

        return new SalesForecastStructuredResponse(SalesForecastResponseMode.ConsultantDetail, language, string.Join(Environment.NewLine, lines));
    }

    private static string FormatMonthYear(int year, int month, SalesForecastLanguage language)
    {
        var culture = language == SalesForecastLanguage.Swedish ? "sv-SE" : "en-US";
        return new DateTime(year, month, 1).ToString("Y", CultureInfo.GetCultureInfo(culture));
    }

    private static string FormatSek(decimal value, SalesForecastLanguage language)
    {
        var culture = language == SalesForecastLanguage.Swedish ? "sv-SE" : "en-US";
        return $"{Decimal.Round(value, 0).ToString("N0", CultureInfo.GetCultureInfo(culture))} SEK";
    }

    private static string FormatHours(double value) =>
        Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal value) =>
        $"{Decimal.Round(value * 100m, 0):N0}%";
}

using System.Globalization;
using HqAgent.Shared.Models;

namespace HqAgent.Agents.SalesForecast.Services;

public static class SalesForecastQuestionInterpreter
{
    private static readonly IReadOnlyDictionary<string, int> MonthNumbers = BuildMonthNumbers();

    public static SalesForecastStructuredRequest? TryInterpret(
        string message,
        IReadOnlyList<ChatTurnEntity> history,
        IReadOnlyList<string> consultantNames,
        DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var language = DetectLanguage(message);
        var normalized = $" {message.Trim().ToLowerInvariant()} ";
        var consultant = ResolveConsultantName(message, consultantNames)
            ?? ResolveConsultantNameFromHistory(history, consultantNames);

        if (TryResolveRestOfYearPeriods(normalized, today, out var rangePeriods))
        {
            return new SalesForecastStructuredRequest(
                SalesForecastResponseMode.MonthlyBreakdown,
                language,
                rangePeriods);
        }

        var period = ResolveSinglePeriod(message, history, today);
        if (period is null)
            return null;

        if (IsBookedVsEstimatedQuestion(normalized))
        {
            return new SalesForecastStructuredRequest(
                SalesForecastResponseMode.BookedVsEstimated,
                language,
                [period]);
        }

        if (consultant is not null && IsConsultantDetailQuestion(normalized))
        {
            return new SalesForecastStructuredRequest(
                SalesForecastResponseMode.ConsultantDetail,
                language,
                [period],
                consultant);
        }

        if (IsMonthlyForecastQuestion(normalized))
        {
            return new SalesForecastStructuredRequest(
                SalesForecastResponseMode.MonthlyOverview,
                language,
                [period]);
        }

        return null;
    }

    public static SalesForecastLanguage DetectLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return SalesForecastLanguage.English;

        var sample = $" {message.Trim().ToLowerInvariant()} ";
        if (sample.IndexOfAny(['å', 'ä', 'ö']) >= 0)
            return SalesForecastLanguage.Swedish;

        string[] swedishMarkers =
        [
            " och ", " hur ", " varför ", " vad ", " nästa ", " månad ", " ge mig ",
            " timmar ", " bokad ", " uppskattat ", " konsult ", " resten av ", " året "
        ];

        return swedishMarkers.Any(sample.Contains)
            ? SalesForecastLanguage.Swedish
            : SalesForecastLanguage.English;
    }

    private static bool TryResolveRestOfYearPeriods(
        string normalized,
        DateOnly today,
        out IReadOnlyList<SalesForecastPeriod> periods)
    {
        periods = [];
        var asksForRestOfYear = normalized.Contains(" rest of ")
            || normalized.Contains(" rest of the year ")
            || normalized.Contains(" resten av ")
            || normalized.Contains(" resten på ");
        var asksForBreakdown = normalized.Contains(" broken down ")
            || normalized.Contains(" per month ")
            || normalized.Contains(" månad för månad ")
            || normalized.Contains(" uppdelat per månad ");

        if (!asksForRestOfYear || !asksForBreakdown)
            return false;

        var year = ExtractYear(normalized) ?? today.Year;
        var startMonth = year == today.Year ? today.Month + 1 : 1;
        if (startMonth > 12)
            return false;

        periods = Enumerable.Range(startMonth, 12 - startMonth + 1)
            .Select(month => new SalesForecastPeriod(year, month))
            .ToArray();
        return periods.Count > 0;
    }

    private static SalesForecastPeriod? ResolveSinglePeriod(
        string message,
        IReadOnlyList<ChatTurnEntity> history,
        DateOnly today)
    {
        if (TryParsePeriod(message, today, out var period))
            return period;

        foreach (var turn in history.Where(t => string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase)).Reverse())
        {
            if (TryParsePeriod(turn.Content, today, out period))
                return period;
        }

        return null;
    }

    private static bool TryParsePeriod(string text, DateOnly today, out SalesForecastPeriod? period)
    {
        period = null;
        var normalized = $" {text.Trim().ToLowerInvariant()} ";

        if (normalized.Contains(" next month ") || normalized.Contains(" nästa månad "))
        {
            var nextMonth = today.AddMonths(1);
            period = new SalesForecastPeriod(nextMonth.Year, nextMonth.Month);
            return true;
        }

        if (normalized.Contains(" this month ") || normalized.Contains(" denna månad ") || normalized.Contains(" den här månaden "))
        {
            period = new SalesForecastPeriod(today.Year, today.Month);
            return true;
        }

        var year = ExtractYear(normalized) ?? today.Year;
        foreach (var monthName in MonthNumbers.Keys.OrderByDescending(k => k.Length))
        {
            if (normalized.Contains($" {monthName} "))
            {
                period = new SalesForecastPeriod(year, MonthNumbers[monthName]);
                return true;
            }
        }

        return false;
    }

    private static int? ExtractYear(string normalized)
    {
        for (var i = 0; i < normalized.Length - 3; i++)
        {
            if (char.IsDigit(normalized[i]) &&
                char.IsDigit(normalized[i + 1]) &&
                char.IsDigit(normalized[i + 2]) &&
                char.IsDigit(normalized[i + 3]))
            {
                var candidate = normalized.Substring(i, 4);
                if (int.TryParse(candidate, CultureInfo.InvariantCulture, out var year))
                    return year;
            }
        }

        return null;
    }

    private static bool IsBookedVsEstimatedQuestion(string normalized) =>
        normalized.Contains(" booked vs estimated ")
        || normalized.Contains(" booked versus estimated ")
        || normalized.Contains(" booked and estimated ")
        || normalized.Contains(" bokat vs uppskattat ")
        || normalized.Contains(" bokat och uppskattat ");

    private static bool IsConsultantDetailQuestion(string normalized) =>
        normalized.Contains(" why ")
        || normalized.Contains(" how ")
        || normalized.Contains(" explain ")
        || normalized.Contains(" details ")
        || normalized.Contains(" fewer hours ")
        || normalized.Contains(" lägre ")
        || normalized.Contains(" varför ")
        || normalized.Contains(" hur ")
        || normalized.Contains(" detaljer ")
        || normalized.Contains(" färre timmar ");

    private static bool IsMonthlyForecastQuestion(string normalized) =>
        normalized.Contains(" forecast ")
        || normalized.Contains(" what does ")
        || normalized.Contains(" what is ")
        || normalized.Contains(" looks like ")
        || normalized.Contains(" tell me ")
        || normalized.Contains(" prognos ")
        || normalized.Contains(" hur ser ")
        || normalized.Contains(" vad ser ")
        || normalized.Contains(" visa ")
        || normalized.Contains(" ge mig ");

    private static string? ResolveConsultantName(string message, IReadOnlyList<string> consultantNames)
    {
        return consultantNames
            .FirstOrDefault(name => message.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveConsultantNameFromHistory(
        IReadOnlyList<ChatTurnEntity> history,
        IReadOnlyList<string> consultantNames)
    {
        foreach (var turn in history.Where(t => string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase)).Reverse())
        {
            var name = ResolveConsultantName(turn.Content, consultantNames);
            if (name is not null)
                return name;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, int> BuildMonthNumbers()
    {
        var cultures = new[]
        {
            CultureInfo.GetCultureInfo("en-US"),
            CultureInfo.GetCultureInfo("sv-SE"),
        };
        var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in cultures)
        {
            AddMonthNames(months, culture.DateTimeFormat.MonthNames);
            AddMonthNames(months, culture.DateTimeFormat.AbbreviatedMonthNames);
        }

        months["sept"] = 9;
        return months;
    }

    private static void AddMonthNames(
        IDictionary<string, int> months,
        IReadOnlyList<string> names)
    {
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            months[name.ToLowerInvariant()] = i + 1;
        }
    }
}

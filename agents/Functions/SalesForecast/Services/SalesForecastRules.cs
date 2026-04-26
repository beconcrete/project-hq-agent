namespace HqAgent.Agents.SalesForecast.Services;

public static class SalesForecastRules
{
    // Forecast assumptions live in one place on purpose. These are business
    // rules that management may want to inspect or change later, so they should
    // not be scattered across multiple methods.

    // Confirmed client work is treated as fully sellable capacity for the
    // month. If a consultant is sold for the month, forecast 100% of the
    // available contract-overlap hours as billable.
    public const decimal BookedUtilizationValue = 1.00m;

    // The first full month after contract end is assumed to be softer while the
    // consultant transitions to a new assignment.
    public const decimal FirstMonthAfterContractEndAdjustment = -0.25m;

    // New hires are assumed to spend their first 45 days onboarding with no
    // billable hours before ramping to the base utilization target.
    public const int OnboardingDays = 45;

    public static decimal NormalizeUtilizationTarget(decimal configuredValue)
    {
        var normalized = configuredValue > 1m ? configuredValue / 100m : configuredValue;
        return ClampUtilization(normalized);
    }

    public static decimal BookedUtilization(decimal _) =>
        BookedUtilizationValue;

    public static decimal FirstMonthAfterContractEndUtilization(decimal baseTarget) =>
        ClampUtilization(baseTarget + FirstMonthAfterContractEndAdjustment);

    public static decimal ClampUtilization(decimal value) =>
        decimal.Clamp(value, 0m, 1m);
}

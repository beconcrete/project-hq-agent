namespace HqAgent.Shared.Models;

public static class HRSalaryCalculator
{
    /// <summary>
    /// Calculates monthly salary and returns a structured result with full breakdown.
    /// Formula: BaseSalary + (BillingBaseRate × max(0, hoursBilled − standardHoursDeduction))
    /// </summary>
    public static SalaryCalculation Calculate(
        decimal baseSalary,
        decimal billingBaseRate,
        decimal hoursBilled,
        int standardHoursDeduction)
    {
        var eligibleHours = Math.Max(0, hoursBilled - standardHoursDeduction);
        var flexibleSalary = billingBaseRate * eligibleHours;
        var total = baseSalary + flexibleSalary;

        return new SalaryCalculation(
            BaseSalary: baseSalary,
            BillingBaseRate: billingBaseRate,
            HoursBilled: hoursBilled,
            StandardHoursDeduction: standardHoursDeduction,
            EligibleHours: eligibleHours,
            FlexibleSalary: flexibleSalary,
            TotalSalary: total);
    }
}

public record SalaryCalculation(
    decimal BaseSalary,
    decimal BillingBaseRate,
    decimal HoursBilled,
    int StandardHoursDeduction,
    decimal EligibleHours,
    decimal FlexibleSalary,
    decimal TotalSalary)
{
    public string FormatSEK() =>
        $"If you bill {HoursBilled} hours, your salary would be {TotalSalary:N0} kr\n" +
        $"Base salary: {BaseSalary:N0} kr + Flexible Salary: {BillingBaseRate:N0} kr/hr × ({HoursBilled} − {StandardHoursDeduction} eligible hrs) = {FlexibleSalary:N0} kr";
}

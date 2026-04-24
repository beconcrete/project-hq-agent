namespace HqAgent.Shared.Models;

public static class HRSalaryCalculator
{
    /// <summary>
    /// Calculates monthly salary and returns a structured result with full breakdown.
    /// Formula: BaseSalary + (BillingBaseRate × max(0, hoursBilled − bonusThreshold))
    /// </summary>
    public static SalaryCalculation Calculate(
        decimal baseSalary,
        decimal billingBaseRate,
        decimal hoursBilled,
        int bonusThreshold)
    {
        var billableHours = Math.Max(0, hoursBilled - bonusThreshold);
        var bonus = billingBaseRate * billableHours;
        var total = baseSalary + bonus;

        return new SalaryCalculation(
            BaseSalary: baseSalary,
            BillingBaseRate: billingBaseRate,
            HoursBilled: hoursBilled,
            BonusThreshold: bonusThreshold,
            BillableHours: billableHours,
            Bonus: bonus,
            TotalSalary: total);
    }
}

public record SalaryCalculation(
    decimal BaseSalary,
    decimal BillingBaseRate,
    decimal HoursBilled,
    int BonusThreshold,
    decimal BillableHours,
    decimal Bonus,
    decimal TotalSalary)
{
    public string FormatSEK() =>
        $"Om du fakturerar {HoursBilled} timmar blir din lön {TotalSalary:N0} kr\n" +
        $"Grundlön: {BaseSalary:N0} kr + Faktureringsbonus: {BillingBaseRate:N0} kr/tim × ({HoursBilled} − {BonusThreshold} tim) = {Bonus:N0} kr";
}

using HqAgent.Shared.Models;
using Xunit;

namespace HqAgent.Shared.Tests;

public class HRSalaryCalculatorTests
{
    [Fact]
    public void CalculatesCorrectlyAboveThreshold()
    {
        var result = HRSalaryCalculator.Calculate(
            baseSalary: 45_000,
            billingBaseRate: 950,
            hoursBilled: 160,
            bonusThreshold: 30);

        // 45000 + (950 × (160 − 30)) = 45000 + 123500 = 168500
        Assert.Equal(45_000m, result.BaseSalary);
        Assert.Equal(130m, result.BillableHours);
        Assert.Equal(123_500m, result.Bonus);
        Assert.Equal(168_500m, result.TotalSalary);
    }

    [Fact]
    public void ReturnsBaseSalaryOnlyWhenAtOrBelowThreshold()
    {
        var result = HRSalaryCalculator.Calculate(
            baseSalary: 45_000,
            billingBaseRate: 950,
            hoursBilled: 30,
            bonusThreshold: 30);

        Assert.Equal(0m, result.BillableHours);
        Assert.Equal(0m, result.Bonus);
        Assert.Equal(45_000m, result.TotalSalary);
    }

    [Fact]
    public void ReturnsBaseSalaryOnlyWhenBelowThreshold()
    {
        var result = HRSalaryCalculator.Calculate(
            baseSalary: 45_000,
            billingBaseRate: 950,
            hoursBilled: 10,
            bonusThreshold: 30);

        Assert.Equal(0m, result.BillableHours);
        Assert.Equal(45_000m, result.TotalSalary);
    }

    [Fact]
    public void RespectsCustomBonusThreshold()
    {
        // If threshold is updated in HRConfig to 40, calculation should change
        var result = HRSalaryCalculator.Calculate(
            baseSalary: 50_000,
            billingBaseRate: 1_000,
            hoursBilled: 160,
            bonusThreshold: 40);

        // 50000 + (1000 × (160 − 40)) = 50000 + 120000 = 170000
        Assert.Equal(120m, result.BillableHours);
        Assert.Equal(120_000m, result.Bonus);
        Assert.Equal(170_000m, result.TotalSalary);
    }

    [Fact]
    public void FormatSEKIncludesBreakdown()
    {
        var result = HRSalaryCalculator.Calculate(45_000, 950, 160, 30);
        var formatted = result.FormatSEK();

        Assert.Contains("168", formatted);   // total
        Assert.Contains("45", formatted);    // base
        Assert.Contains("160", formatted);   // hours
        Assert.Contains("30", formatted);    // threshold
    }
}

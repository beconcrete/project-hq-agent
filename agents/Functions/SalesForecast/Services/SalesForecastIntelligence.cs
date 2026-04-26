using HqAgent.Agents.Contract.Services;
using HqAgent.Agents.HR.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;

namespace HqAgent.Agents.SalesForecast.Services;

public class SalesForecastIntelligence : ISalesForecastIntelligence
{
    private readonly IHRIntelligence _hr;
    private readonly IContractIntelligence _contracts;
    private readonly ForecastTableStorageService _forecastTables;

    public SalesForecastIntelligence(
        IHRIntelligence hr,
        IContractIntelligence contracts,
        ForecastTableStorageService forecastTables)
    {
        _hr = hr;
        _contracts = contracts;
        _forecastTables = forecastTables;
    }

    public async Task<MonthlyForecastSummary> GetMonthlyForecastAsync(int year, int month, CancellationToken ct)
    {
        ValidatePeriod(year, month);

        var employees = await _hr.ListEmployeesAsync(ct);
        var consultants = new List<ForecastResult>(employees.Count);

        foreach (var employee in employees)
        {
            var result = await BuildForecastAsync(employee, year, month, ct);
            consultants.Add(result);
        }

        return new MonthlyForecastSummary
        {
            Year = year,
            Month = month,
            TotalBookedRevenue = consultants
                .Where(c => c.Status == ForecastStatus.Booked)
                .Sum(c => c.EstimatedRevenueSEK),
            TotalUnbookedEstimate = consultants
                .Where(c => c.Status == ForecastStatus.Unbooked)
                .Sum(c => c.EstimatedRevenueSEK),
            BookedHeadcount = consultants.Count(c => c.Status == ForecastStatus.Booked),
            UnbookedHeadcount = consultants.Count(c => c.Status == ForecastStatus.Unbooked),
            Consultants = consultants.OrderBy(c => c.Name).ToList(),
        };
    }

    public async Task<ForecastResult?> GetConsultantForecastAsync(
        string consultantName,
        int year,
        int month,
        CancellationToken ct)
    {
        ValidatePeriod(year, month);
        if (string.IsNullOrWhiteSpace(consultantName))
            return null;

        var employees = await _hr.ListEmployeesAsync(ct);
        var employee = employees.FirstOrDefault(e =>
            string.Equals(e.FullName, consultantName, StringComparison.OrdinalIgnoreCase))
            ?? employees.FirstOrDefault(e =>
                e.FullName.Contains(consultantName, StringComparison.OrdinalIgnoreCase));

        return employee is null
            ? null
            : await BuildForecastAsync(employee, year, month, ct);
    }

    private async Task<ForecastResult> BuildForecastAsync(
        EmployeeSummary employee,
        int year,
        int month,
        CancellationToken ct)
    {
        var workingHours = await _forecastTables.GetWorkingHoursAsync(month, ct)
            ?? throw new InvalidOperationException($"Working hours are not seeded for {year}-{month:D2}.");

        var seniorityLevel = ResolveSeniorityLevel(employee);
        var contract = await _contracts.FindContractForPeriodAsync(
            employee.FullName,
            year,
            month,
            new ContractCallerContext("sales-forecast", true),
            ct);

        if (contract is not null)
        {
            var billableHours = contract.StartDate > new DateOnly(year, month, 1)
                ? CountWeekdays(contract.StartDate, LastDayOfMonth(year, month)) * 8d
                : workingHours.AvailableHours;
            var hourlyRate = contract.HourlyRateSEK ?? employee.BillingBaseRate;

            return new ForecastResult
            {
                ConsultantId = employee.EmployeeId,
                Name = employee.FullName,
                SeniorityLevel = seniorityLevel,
                Status = ForecastStatus.Booked,
                BillableHours = billableHours,
                HourlyRate = hourlyRate,
                EstimatedRevenueSEK = Decimal.Round((decimal)billableHours * hourlyRate, 2),
            };
        }

        var rate = await _forecastTables.GetSeniorityRateAsync(seniorityLevel, ct)
            ?? throw new InvalidOperationException($"Seniority rate is not seeded for '{seniorityLevel}'.");
        var unbookedHours = workingHours.AvailableHours * rate.Utilization;

        return new ForecastResult
        {
            ConsultantId = employee.EmployeeId,
            Name = employee.FullName,
            SeniorityLevel = seniorityLevel,
            Status = ForecastStatus.Unbooked,
            BillableHours = unbookedHours,
            HourlyRate = rate.HourlyRateSEK,
            EstimatedRevenueSEK = Decimal.Round((decimal)unbookedHours * rate.HourlyRateSEK, 2),
        };
    }

    private static string ResolveSeniorityLevel(EmployeeSummary employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.SeniorityLevel))
            return NormalizeSeniority(employee.SeniorityLevel);

        if (employee.BillingBaseRate >= 1300m)
            return "Senior";
        if (employee.BillingBaseRate >= 1000m)
            return "Medior";
        return "Junior";
    }

    private static string NormalizeSeniority(string seniorityLevel)
    {
        var normalized = seniorityLevel.Trim().ToLowerInvariant();
        return normalized switch
        {
            "senior" => "Senior",
            "medior" or "mid" or "middle" => "Medior",
            "junior" => "Junior",
            _ => seniorityLevel.Trim(),
        };
    }

    private static void ValidatePeriod(int year, int month)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be positive.");
    }

    private static DateOnly LastDayOfMonth(int year, int month) =>
        new(year, month, DateTime.DaysInMonth(year, month));

    private static int CountWeekdays(DateOnly from, DateOnly to)
    {
        if (to < from)
            return 0;

        var count = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }

        return count;
    }
}

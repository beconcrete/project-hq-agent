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
        var hrConfig = await _hr.GetHRConfigAsync(ct);
        var utilizationTarget = SalesForecastRules.NormalizeUtilizationTarget(hrConfig.UtilizationTarget);
        var seniorityRates = await LoadSeniorityRatesAsync(ct);
        var consultants = new List<ForecastResult>(employees.Count);

        foreach (var employee in employees)
        {
            var result = await BuildForecastAsync(
                employee,
                year,
                month,
                utilizationTarget,
                seniorityRates,
                ct);
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
        var hrConfig = await _hr.GetHRConfigAsync(ct);
        var utilizationTarget = SalesForecastRules.NormalizeUtilizationTarget(hrConfig.UtilizationTarget);
        var seniorityRates = await LoadSeniorityRatesAsync(ct);
        var employee = employees.FirstOrDefault(e =>
            string.Equals(e.FullName, consultantName, StringComparison.OrdinalIgnoreCase))
            ?? employees.FirstOrDefault(e =>
                e.FullName.Contains(consultantName, StringComparison.OrdinalIgnoreCase));

        return employee is null
            ? null
            : await BuildForecastAsync(
                employee,
                year,
                month,
                utilizationTarget,
                seniorityRates,
                ct);
    }

    private async Task<ForecastResult> BuildForecastAsync(
        EmployeeSummary employee,
        int year,
        int month,
        decimal baseUtilizationTarget,
        IReadOnlyDictionary<string, SeniorityRateEntity> seniorityRates,
        CancellationToken ct)
    {
        var workingHours = await _forecastTables.GetWorkingHoursAsync(month, ct)
            ?? throw new InvalidOperationException($"Working hours are not seeded for {year}-{month:D2}.");

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = LastDayOfMonth(year, month);
        var contractHistory = await LoadForecastContractsAsync(employee, ct);
        var contract = await _contracts.FindContractForPeriodAsync(
            employee.FullName,
            year,
            month,
            new ContractCallerContext("sales-forecast", true),
            ct);

        if (contract is not null)
        {
            var overlapStart = contract.StartDate > periodStart ? contract.StartDate : periodStart;
            var overlapEnd = contract.EndDate < periodEnd ? contract.EndDate : periodEnd;
            var availableContractHours = CountWeekdays(overlapStart, overlapEnd) * 8d;
            var billableHours = availableContractHours *
                (double)SalesForecastRules.BookedUtilization(baseUtilizationTarget);
            var hourlyRate = contract.HourlyRateSEK ?? employee.BillingBaseRate;
            var bookedSeniorityLevel = ResolveSeniorityLevel(
                employee,
                seniorityRates,
                contractHistory,
                hourlyRate);

            return new ForecastResult
            {
                ConsultantId = employee.EmployeeId,
                Name = employee.FullName,
                SeniorityLevel = bookedSeniorityLevel,
                Status = ForecastStatus.Booked,
                BillableHours = billableHours,
                HourlyRate = hourlyRate,
                EstimatedRevenueSEK = Decimal.Round((decimal)billableHours * hourlyRate, 2),
            };
        }

        var seniorityLevel = ResolveSeniorityLevel(
            employee,
            seniorityRates,
            contractHistory,
            null);
        var rate = seniorityRates.TryGetValue(seniorityLevel, out var resolvedRate)
            ? resolvedRate
            : throw new InvalidOperationException($"Seniority rate is not seeded for '{seniorityLevel}'.");
        var utilization = ResolveUnbookedUtilization(
            employee,
            periodStart,
            periodEnd,
            contractHistory,
            baseUtilizationTarget);
        var unbookedHours = workingHours.AvailableHours * (double)utilization;

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

    private static decimal ResolveUnbookedUtilization(
        EmployeeSummary employee,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<ContractSummary> contractHistory,
        decimal baseUtilizationTarget)
    {
        var onboardingUtilization = ResolveOnboardingUtilization(
            employee,
            periodStart,
            periodEnd,
            baseUtilizationTarget);
        if (onboardingUtilization.HasValue)
            return onboardingUtilization.Value;

        var lastContractEnd = contractHistory
            .Select(ContractEndDate)
            .Where(date => date.HasValue && date.Value < periodStart)
            .Select(date => date!.Value)
            .DefaultIfEmpty()
            .Max();

        if (lastContractEnd != default &&
            lastContractEnd.AddMonths(1).Year == periodStart.Year &&
            lastContractEnd.AddMonths(1).Month == periodStart.Month)
            return SalesForecastRules.FirstMonthAfterContractEndUtilization(baseUtilizationTarget);

        return baseUtilizationTarget;
    }

    private static decimal? ResolveOnboardingUtilization(
        EmployeeSummary employee,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal baseUtilizationTarget)
    {
        var startDate = DateOnly.FromDateTime(employee.StartDate.UtcDateTime.Date);
        var onboardingEnd = startDate.AddDays(SalesForecastRules.OnboardingDays - 1);

        if (periodStart > onboardingEnd)
            return null;

        if (periodEnd <= onboardingEnd)
            return 0m;

        var totalWeekdays = CountWeekdays(periodStart, periodEnd);
        if (totalWeekdays == 0)
            return 0m;

        var firstBillableDay = onboardingEnd.AddDays(1);
        var billableWeekdays = CountWeekdays(
            firstBillableDay > periodStart ? firstBillableDay : periodStart,
            periodEnd);
        var monthlyAvailableHours = totalWeekdays * 8m;
        var billableHours = billableWeekdays * 8m * baseUtilizationTarget;
        return monthlyAvailableHours == 0m
            ? 0m
            : SalesForecastRules.ClampUtilization(billableHours / monthlyAvailableHours);
    }

    private static string ResolveSeniorityLevel(
        EmployeeSummary employee,
        IReadOnlyDictionary<string, SeniorityRateEntity> seniorityRates,
        IReadOnlyList<ContractSummary> contractHistory,
        decimal? activeContractRate)
    {
        if (!string.IsNullOrWhiteSpace(employee.SeniorityLevel))
            return NormalizeSeniority(employee.SeniorityLevel);

        var referenceRate = activeContractRate
            ?? contractHistory
                .Select(ContractHourlyRate)
                .Where(rate => rate.HasValue)
                .Select(rate => rate!.Value)
                .OrderByDescending(rate => rate)
                .FirstOrDefault();

        if (referenceRate > 0m)
            return ClosestSeniorityForRate(referenceRate, seniorityRates);

        if (employee.BillingBaseRate > 0m)
            return ClosestSeniorityForRate(employee.BillingBaseRate, seniorityRates);

        return "Medior";
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

    private async Task<IReadOnlyDictionary<string, SeniorityRateEntity>> LoadSeniorityRatesAsync(CancellationToken ct)
    {
        var roles = new[] { "Senior", "Medior", "Junior" };
        var rates = new Dictionary<string, SeniorityRateEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            rates[role] = await _forecastTables.GetSeniorityRateAsync(role, ct)
                ?? throw new InvalidOperationException($"Seniority rate is not seeded for '{role}'.");
        }

        return rates;
    }

    private async Task<IReadOnlyList<ContractSummary>> LoadForecastContractsAsync(
        EmployeeSummary employee,
        CancellationToken ct)
    {
        var contracts = await _contracts.FindByPersonAsync(
            new ContractCallerContext("sales-forecast", true),
            employee.FullName,
            ct);

        return contracts
            .Where(IsForecastRelevantContract)
            .ToArray();
    }

    private static bool IsForecastRelevantContract(ContractSummary contract)
    {
        if (string.Equals(contract.ReviewState, "pending_review", StringComparison.OrdinalIgnoreCase))
            return false;

        return ContractStartDate(contract).HasValue &&
            ContractEndDate(contract).HasValue &&
            ContractHourlyRate(contract).HasValue;
    }

    private static DateOnly? ContractStartDate(ContractSummary contract) =>
        contract.AssignmentStartDate is DateTime assignmentStart
            ? DateOnly.FromDateTime(assignmentStart)
            : contract.EffectiveDate is DateTime effectiveDate
                ? DateOnly.FromDateTime(effectiveDate)
                : null;

    private static DateOnly? ContractEndDate(ContractSummary contract) =>
        contract.AssignmentEndDate is DateTime assignmentEnd
            ? DateOnly.FromDateTime(assignmentEnd)
            : contract.ExpiryDate is DateTime expiryDate
                ? DateOnly.FromDateTime(expiryDate)
                : null;

    private static decimal? ContractHourlyRate(ContractSummary contract) =>
        contract.PaymentAmount.HasValue &&
        string.Equals(contract.PaymentCurrency, "SEK", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(contract.PaymentUnit, "hour", StringComparison.OrdinalIgnoreCase)
            ? (decimal)contract.PaymentAmount.Value
            : null;

    private static string ClosestSeniorityForRate(
        decimal referenceRate,
        IReadOnlyDictionary<string, SeniorityRateEntity> seniorityRates)
    {
        return seniorityRates
            .Select(kvp => new
            {
                Role = kvp.Key,
                Distance = Math.Abs(kvp.Value.HourlyRateSEK - referenceRate),
            })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Role)
            .First()
            .Role;
    }
}

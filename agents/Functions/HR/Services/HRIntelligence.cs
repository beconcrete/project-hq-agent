using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.HR.Services;

public class HRIntelligence : IHRIntelligence
{
    private readonly HRTableStorageService _storage;
    private readonly ILogger<HRIntelligence> _logger;

    public HRIntelligence(HRTableStorageService storage, ILogger<HRIntelligence> logger)
    {
        _storage = storage;
        _logger  = logger;
    }

    public async Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(CancellationToken ct)
    {
        var employees = await _storage.ListEmployeesAsync(includeOffboarded: false, ct);
        return employees.Select(ToSummary).ToList();
    }

    public async Task<EmployeeSummary?> FindEmployeeAsync(string nameOrEmail, CancellationToken ct)
    {
        var employees = await _storage.ListEmployeesAsync(includeOffboarded: true, ct);
        var normalized = nameOrEmail.Trim().ToLowerInvariant();

        var match = employees.FirstOrDefault(e =>
            e.FullName.ToLowerInvariant().Contains(normalized) ||
            e.Email.ToLowerInvariant().Contains(normalized));

        return match is null ? null : ToSummary(match);
    }

    public async Task<EmployeeSummary> AddEmployeeAsync(AddEmployeeRequest request, CancellationToken ct)
    {
        var entity = new EmployeeEntity
        {
            RowKey          = Guid.NewGuid().ToString(),
            FullName        = request.FullName,
            Email           = request.Email,
            StartDate       = request.StartDate,
            Status          = "active",
            BaseSalary      = request.BaseSalary,
            BillingBaseRate = request.BillingBaseRate,
            VacationBalance = request.VacationBalance,
        };

        await _storage.WriteEmployeeAsync(entity, ct);
        _logger.LogInformation("Added employee {FullName} ({EmployeeId})", entity.FullName, entity.RowKey);
        return ToSummary(entity);
    }

    public async Task<EmployeeSummary?> UpdateEmployeeAsync(
        string employeeId, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var entity = await _storage.GetEmployeeAsync(employeeId, ct);
        if (entity is null) return null;

        if (request.FullName        is not null) entity.FullName        = request.FullName;
        if (request.Email           is not null) entity.Email           = request.Email;
        if (request.StartDate       is not null) entity.StartDate       = request.StartDate.Value;
        if (request.BaseSalary      is not null) entity.BaseSalary      = request.BaseSalary.Value;
        if (request.BillingBaseRate is not null) entity.BillingBaseRate = request.BillingBaseRate.Value;
        if (request.VacationBalance is not null) entity.VacationBalance = request.VacationBalance.Value;

        await _storage.WriteEmployeeAsync(entity, ct);
        return ToSummary(entity);
    }

    public async Task<EmployeeSummary?> OffboardEmployeeAsync(
        string employeeId, DateTimeOffset offboardDate, CancellationToken ct)
    {
        var entity = await _storage.GetEmployeeAsync(employeeId, ct);
        if (entity is null) return null;

        entity.Status       = "offboarded";
        entity.OffboardDate = offboardDate;

        await _storage.WriteEmployeeAsync(entity, ct);
        _logger.LogInformation("Offboarded employee {FullName} ({EmployeeId})", entity.FullName, employeeId);
        return ToSummary(entity);
    }

    public async Task<SalaryResult?> CalculateSalaryAsync(
        string employeeId, decimal hoursBilled, CancellationToken ct)
    {
        var entity = await _storage.GetEmployeeAsync(employeeId, ct);
        if (entity is null) return null;

        // Always reads fresh from Table Storage — never uses cached or hardcoded values
        var config = await _storage.GetHRConfigAsync(ct);

        var calc = HRSalaryCalculator.Calculate(
            entity.BaseSalary,
            entity.BillingBaseRate,
            hoursBilled,
            config.BonusThreshold);

        return new SalaryResult(
            EmployeeId:     employeeId,
            FullName:       entity.FullName,
            HoursBilled:    hoursBilled,
            BaseSalary:     calc.BaseSalary,
            BillingBaseRate: calc.BillingBaseRate,
            BonusThreshold: calc.BonusThreshold,
            BillableHours:  calc.BillableHours,
            Bonus:          calc.Bonus,
            TotalSalary:    calc.TotalSalary,
            Breakdown:      calc.FormatSEK());
    }

    public async Task<HRConfigSummary> GetHRConfigAsync(CancellationToken ct)
    {
        var config = await _storage.GetHRConfigAsync(ct);
        return new HRConfigSummary(config.BonusThreshold, config.UtilizationTarget);
    }

    private static EmployeeSummary ToSummary(EmployeeEntity e) => new(
        EmployeeId:     e.RowKey,
        FullName:       e.FullName,
        Email:          e.Email,
        StartDate:      e.StartDate,
        Status:         e.Status,
        OffboardDate:   e.OffboardDate,
        BaseSalary:     e.BaseSalary,
        BillingBaseRate: e.BillingBaseRate,
        VacationBalance: e.VacationBalance);
}

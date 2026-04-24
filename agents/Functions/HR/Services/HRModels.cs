namespace HqAgent.Agents.HR.Services;

public record EmployeeSummary(
    string EmployeeId,
    string FullName,
    string Email,
    DateTimeOffset StartDate,
    string Status,
    DateTimeOffset? OffboardDate,
    decimal BaseSalary,
    decimal BillingBaseRate,
    int VacationBalance);

public record SalaryResult(
    string EmployeeId,
    string FullName,
    decimal HoursBilled,
    decimal BaseSalary,
    decimal BillingBaseRate,
    int StandardHoursDeduction,
    decimal EligibleHours,
    decimal FlexibleSalary,
    decimal TotalSalary,
    string Breakdown);

public record HRConfigSummary(
    int StandardHoursDeduction,
    decimal UtilizationTarget);

public record AddEmployeeRequest(
    string FullName,
    string Email,
    DateTimeOffset StartDate,
    decimal BaseSalary,
    decimal BillingBaseRate,
    int VacationBalance = 30);

public record UpdateEmployeeRequest(
    string? FullName = null,
    string? Email = null,
    DateTimeOffset? StartDate = null,
    decimal? BaseSalary = null,
    decimal? BillingBaseRate = null,
    int? VacationBalance = null);

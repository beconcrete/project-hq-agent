namespace HqAgent.Agents.HR.Services;

public record EmployeeSummary(
    string EmployeeId,
    string FullName,
    string WorkEmail,
    DateTimeOffset StartDate,
    string Status,
    DateTimeOffset? OffboardDate,
    decimal BaseSalary,
    decimal BillingBaseRate,
    string SeniorityLevel,
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
    string WorkEmail,
    DateTimeOffset StartDate,
    decimal BaseSalary,
    decimal BillingBaseRate,
    string? SeniorityLevel = null,
    int VacationBalance = 30);

public record UpdateEmployeeRequest(
    string? FullName = null,
    string? WorkEmail = null,
    DateTimeOffset? StartDate = null,
    decimal? BaseSalary = null,
    decimal? BillingBaseRate = null,
    string? SeniorityLevel = null,
    int? VacationBalance = null);

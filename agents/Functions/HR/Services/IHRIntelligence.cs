namespace HqAgent.Agents.HR.Services;

public interface IHRIntelligence
{
    Task<IReadOnlyList<EmployeeSummary>> ListEmployeesAsync(CancellationToken ct);
    Task<IReadOnlyList<EmployeeSummary>> FindEmployeesAsync(string nameOrEmail, CancellationToken ct);
    Task<EmployeeSummary> AddEmployeeAsync(AddEmployeeRequest request, CancellationToken ct);
    Task<EmployeeSummary?> UpdateEmployeeAsync(string employeeId, UpdateEmployeeRequest request, CancellationToken ct);
    Task<EmployeeSummary?> OffboardEmployeeAsync(string employeeId, DateTimeOffset offboardDate, CancellationToken ct);
    Task<SalaryResult?> CalculateSalaryAsync(string employeeId, decimal hoursBilled, CancellationToken ct);
    Task<HRConfigSummary> GetHRConfigAsync(CancellationToken ct);
}

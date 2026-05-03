using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "employees", RowKey = employeeId (GUID, immutable — never derived from email)
public class EmployeeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "employees";
    public string RowKey { get; set; } = string.Empty; // employeeId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;    // work identity, e.g. bjorn.eriksen@beconcrete.se
    public string LoginEmail { get; set; } = string.Empty;   // Auth0 JWT email — may differ from WorkEmail
    public string Auth0Subject { get; set; } = string.Empty; // Auth0 sub claim, e.g. google-oauth2|105...
    public DateTimeOffset StartDate { get; set; }
    public string Status { get; set; } = "active"; // "active" | "offboarded"
    public DateTimeOffset? OffboardDate { get; set; }
    public double BaseSalary { get; set; }       // SEK — double because Table Storage does not support decimal
    public double BillingBaseRate { get; set; }  // SEK/hr
    public string SeniorityLevel { get; set; } = string.Empty;
    public int VacationBalance { get; set; } = 30; // days
}

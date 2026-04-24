using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "employees", RowKey = EmployeeId (GUID)
public class EmployeeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "employees";
    public string RowKey { get; set; } = string.Empty; // EmployeeId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public string Status { get; set; } = "active"; // "active" | "offboarded"
    public DateTimeOffset? OffboardDate { get; set; }
    public decimal BaseSalary { get; set; }       // SEK
    public decimal BillingBaseRate { get; set; }  // SEK/hr
    public int VacationBalance { get; set; } = 30; // days
}

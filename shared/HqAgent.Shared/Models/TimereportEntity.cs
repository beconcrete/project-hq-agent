using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = employeeId (GUID), RowKey = {date:yyyyMMdd}_{projectId}_{ticks:D20}
// Multiple entries per employee+project+day are allowed — ticks suffix makes each row unique.
public class TimereportEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // employeeId (GUID)
    public string RowKey { get; set; } = string.Empty;       // {yyyyMMdd}_{projectId}_{ticks:D20}
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string EmployeeId { get; set; } = string.Empty;  // same as PartitionKey, for readability
    public string WorkEmail { get; set; } = string.Empty;   // denormalised — for display only
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;  // denormalised
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty; // denormalised
    public double Hours { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
    public string ReportDate { get; set; } = string.Empty;   // ISO yyyy-MM-dd, for easy filtering
}

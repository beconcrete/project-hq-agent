using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "projects", RowKey = projectId (GUID)
public class ProjectEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "projects";
    public string RowKey { get; set; } = string.Empty; // projectId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty; // denormalised
    public string Status { get; set; } = "active"; // "active" | "closed"
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Description { get; set; } = string.Empty;
    // JSON array of employeeId GUIDs
    public string EmployeeIds { get; set; } = "[]";
}

using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "customers", RowKey = customerId (GUID)
public class CustomerEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "customers";
    public string RowKey { get; set; } = string.Empty; // customerId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string OrgNumber { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string PrimaryContactName { get; set; } = string.Empty;
    public string PrimaryContactEmail { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; // "active" | "inactive"
    public string Notes { get; set; } = string.Empty;
}

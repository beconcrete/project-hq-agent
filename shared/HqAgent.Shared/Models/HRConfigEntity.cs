using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "hrconfig", RowKey = "default" — single config row, always read fresh
public class HRConfigEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "hrconfig";
    public string RowKey { get; set; } = "default";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Hours deducted from billed hours before Flexible Salary applies: BaseSalary + BillingBaseRate × (hoursBilled − StandardHoursDeduction)
    public int StandardHoursDeduction { get; set; } = 30;

    // Target utilization percentage (e.g. 85 = 85%) — double because Table Storage does not support decimal
    public double UtilizationTarget { get; set; } = 85;
}

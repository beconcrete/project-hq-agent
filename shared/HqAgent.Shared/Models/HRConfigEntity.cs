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

    // Hours above which billing bonus applies: BaseSalary + BillingBaseRate × (hoursBilled − BonusThreshold)
    public int BonusThreshold { get; set; } = 30;

    // Target utilization percentage (e.g. 85 = 85%)
    public decimal UtilizationTarget { get; set; } = 85;
}

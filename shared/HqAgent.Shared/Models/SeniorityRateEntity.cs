using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

public class SeniorityRateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "default";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public decimal HourlyRateSEK { get; set; }
    public double Utilization { get; set; }
    public double MixPercent { get; set; }
}

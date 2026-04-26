using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

public class WorkingHoursEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "2026";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int WorkingDays { get; set; }
    public int VacationDays { get; set; }
    public int AvailableDays { get; set; }
    public int AvailableHours { get; set; }
}

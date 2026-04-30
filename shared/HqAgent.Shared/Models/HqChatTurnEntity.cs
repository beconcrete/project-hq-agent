using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = userId, RowKey = {sessionId}_{ticks:D20}
// Enables efficient per-user session loads with chronological ordering within a session.
public class HqChatTurnEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // {sessionId}_{ticks:D20}
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;    // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";    // "contracts" | "hr" | "timereport" | "general"
}

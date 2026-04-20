using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

/// <summary>
/// One turn of a contract chat session stored in Table Storage.
/// PartitionKey = sessionId, RowKey = DateTime.UtcNow.Ticks (zero-padded) for sort order.
/// </summary>
public class ChatTurnEntity : ITableEntity
{
    public string          PartitionKey { get; set; } = string.Empty;
    public string          RowKey       { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp    { get; set; }
    public ETag            ETag         { get; set; }

    public string Role    { get; set; } = string.Empty; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}

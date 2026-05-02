using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = entityType ("employee", "customer", "project", "contract")
// RowKey       = entityId (GUID for customers/projects/contracts, lowercase email for employees)
public class EmbeddingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? Vector { get; set; }        // JSON float[1536], null when Status=pending
    public string Status { get; set; } = "pending"; // "ok" | "pending"
    public string Snippet { get; set; } = string.Empty; // first 150 chars of embedding text
    public DateTimeOffset? LastIndexed { get; set; }
}

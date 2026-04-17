using Azure;
using Azure.Data.Tables;

namespace ContractOrchestratorAgent.Models;

/// <summary>
/// Table Storage entity written to ContractExtractions.
/// PartitionKey = correlationId, RowKey = "extraction"
/// </summary>
public class ContractExtractionEntity : ITableEntity
{
    // ITableEntity
    public string          PartitionKey { get; set; } = string.Empty;
    public string          RowKey       { get; set; } = "extraction";
    public DateTimeOffset? Timestamp    { get; set; }
    public ETag            ETag         { get; set; }

    // Business fields
    public string   BlobPath     { get; set; } = string.Empty;
    public string   DocumentType { get; set; } = string.Empty;

    /// <summary>Full ExtractionResult serialised as JSON.</summary>
    public string   Fields       { get; set; } = string.Empty;

    public string   ModelUsed    { get; set; } = string.Empty;
    public DateTime ProcessedAt  { get; set; }
    public string   Status       { get; set; } = string.Empty;
}

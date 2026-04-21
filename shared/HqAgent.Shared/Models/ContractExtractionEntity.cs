using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

/// <summary>
/// Table Storage entity written to the Contracts table.
/// PartitionKey = correlationId, RowKey = "extraction"
/// </summary>
public class ContractExtractionEntity : ITableEntity
{
    public string          PartitionKey { get; set; } = string.Empty;
    public string          RowKey       { get; set; } = "extraction";
    public DateTimeOffset? Timestamp    { get; set; }
    public ETag            ETag         { get; set; }

    public string   BlobPath             { get; set; } = string.Empty;
    public string   UserId               { get; set; } = string.Empty;
    public string   FileName             { get; set; } = string.Empty;
    public DateTime UploadedAt           { get; set; }
    public string   DocumentType         { get; set; } = string.Empty;
    public double   TriageConfidence     { get; set; }
    public double   ExtractionConfidence { get; set; }

    public DateTime? EffectiveDate        { get; set; }
    public DateTime? ExpiryDate           { get; set; }
    public int?      NoticePeriodDays     { get; set; }
    public DateTime? NoticeDeadline       { get; set; }
    public bool?     AutoRenewal          { get; set; }

    public string   PrimaryCounterparty   { get; set; } = string.Empty;
    public string   CounterpartyNames     { get; set; } = string.Empty;
    public string   PeopleMentioned       { get; set; } = string.Empty;
    public string   CustomerName          { get; set; } = string.Empty;
    public DateTime? AssignmentStartDate  { get; set; }
    public DateTime? AssignmentEndDate    { get; set; }
    public string   RiskFlags             { get; set; } = string.Empty;
    public string   MissingFields         { get; set; } = string.Empty;

    /// <summary>Full ExtractionResult serialised as JSON.</summary>
    public string   Fields               { get; set; } = string.Empty;

    public string    ModelUsed            { get; set; } = string.Empty;
    public DateTime? ProcessedAt         { get; set; }
    public string    Status              { get; set; } = string.Empty;
}

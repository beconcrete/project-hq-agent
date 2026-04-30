using Azure;
using Azure.Data.Tables;

namespace HqAgent.Shared.Models;

// PartitionKey = "contracts", RowKey = contractId (GUID — same value as the old correlationId)
public class ContractEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "contracts";
    public string RowKey { get; set; } = string.Empty; // contractId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BlobPath { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public double TriageConfidence { get; set; }
    public double ExtractionConfidence { get; set; }

    public DateTime? EffectiveDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? NoticePeriodDays { get; set; }
    public DateTime? NoticeDeadline { get; set; }
    public bool? AutoRenewal { get; set; }

    public string PrimaryCounterparty { get; set; } = string.Empty;
    public string CounterpartyNames { get; set; } = string.Empty;
    public string PeopleMentioned { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime? AssignmentStartDate { get; set; }
    public DateTime? AssignmentEndDate { get; set; }
    public double? PaymentAmount { get; set; }
    public string PaymentCurrency { get; set; } = string.Empty;
    public string PaymentUnit { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string RiskFlags { get; set; } = string.Empty;
    public string MissingFields { get; set; } = string.Empty;

    public string Fields { get; set; } = string.Empty;

    public string ModelUsed { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public int? RetryCount { get; set; }
    public string ReviewState { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = string.Empty;
    public string ReviewNote { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public string DuplicateOfCorrelationId { get; set; } = string.Empty;
    public string SupersedesCorrelationId { get; set; } = string.Empty;
    public string RelatedContractIds { get; set; } = string.Empty;
    public string RelationshipReasons { get; set; } = string.Empty;
    public string RelationshipCandidates { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }
    public string DeletedBy { get; set; } = string.Empty;
    public string DeleteReason { get; set; } = string.Empty;
    public string ManualPartyOverride { get; set; } = string.Empty;

    // JSON arrays — populated by customer-linking step during ingestion
    public string LinkedCustomerIds   { get; set; } = "[]";
    public string LinkedCustomerNames { get; set; } = "[]";
}

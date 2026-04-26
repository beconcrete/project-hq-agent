using HqAgent.Shared.Models;

namespace HqAgent.Agents.Contract.Services;

public record ContractCallerContext(string UserId, bool IsAdmin);

public record ContractQuestion(
    string Question,
    string? ContextCorrelationId,
    ContractCallerContext Caller);

public record ContractAnswer(
    string Answer,
    IReadOnlyList<ContractSummary> Contracts);

public record ContractSummary(
    string CorrelationId,
    string FileName,
    string Status,
    string DocumentType,
    DateTime UploadedAt,
    DateTime? EffectiveDate,
    DateTime? ExpiryDate,
    DateTime? NoticeDeadline,
    int? NoticePeriodDays,
    bool? AutoRenewal,
    string PrimaryCounterparty,
    IReadOnlyList<string> CounterpartyNames,
    IReadOnlyList<string> PeopleMentioned,
    string CustomerName,
    DateTime? AssignmentStartDate,
    DateTime? AssignmentEndDate,
    double? PaymentAmount,
    string PaymentCurrency,
    string PaymentUnit,
    string PaymentType,
    string PaymentTerms,
    IReadOnlyList<string> RiskFlags,
    IReadOnlyList<string> MissingFields,
    string ReviewState,
    string RelationshipType,
    string DuplicateOfCorrelationId,
    string SupersedesCorrelationId,
    IReadOnlyList<string> RelatedContractIds,
    IReadOnlyList<string> RelationshipReasons);

public record ContractDetail(
    ContractSummary Summary,
    string ExtractedFieldsJson,
    ContractExtractionEntity Entity);

public record ContractReference(
    string CorrelationId,
    string FileName,
    string DocumentType);

public record ContractPeriodResult(
    string ConsultantName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? HourlyRateSEK,
    string ContractId);

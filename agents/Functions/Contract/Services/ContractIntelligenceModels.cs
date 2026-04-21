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
    IReadOnlyList<string> RiskFlags,
    IReadOnlyList<string> MissingFields);

public record ContractDetail(
    ContractSummary Summary,
    string ExtractedFieldsJson,
    ContractExtractionEntity Entity);

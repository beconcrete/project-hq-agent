namespace HqAgent.Shared.Models;

public record NormalizedContractFacts(
    DateTime? EffectiveDate,
    DateTime? ExpiryDate,
    int? NoticePeriodDays,
    DateTime? NoticeDeadline,
    bool? AutoRenewal,
    string PrimaryCounterparty,
    IReadOnlyList<string> CounterpartyNames,
    IReadOnlyList<string> PeopleMentioned,
    string CustomerName,
    DateTime? AssignmentStartDate,
    DateTime? AssignmentEndDate,
    decimal? PaymentAmount,
    string PaymentCurrency,
    string PaymentUnit,
    string PaymentType,
    string PaymentTerms,
    IReadOnlyList<string> RiskFlags,
    IReadOnlyList<string> MissingFields);

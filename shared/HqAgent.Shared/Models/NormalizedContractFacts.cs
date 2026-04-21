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
    IReadOnlyList<string> RiskFlags,
    IReadOnlyList<string> MissingFields);

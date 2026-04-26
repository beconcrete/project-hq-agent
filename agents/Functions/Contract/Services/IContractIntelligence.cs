namespace HqAgent.Agents.Contract.Services;

public interface IContractIntelligence
{
    Task<IReadOnlyList<ContractSummary>> ListContractsAsync(ContractCallerContext caller, CancellationToken ct);
    Task<ContractDetail?> GetContractAsync(string correlationId, ContractCallerContext caller, CancellationToken ct);
    Task<string?> GetContractDocumentTextAsync(string correlationId, ContractCallerContext caller, CancellationToken ct);
    Task<IReadOnlyList<ContractSummary>> FindExpiringAsync(ContractCallerContext caller, DateOnly? from, DateOnly? to, string? contractType, CancellationToken ct);
    Task<IReadOnlyList<ContractSummary>> FindRenewalWindowsAsync(ContractCallerContext caller, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<IReadOnlyList<ContractSummary>> FindByPersonAsync(ContractCallerContext caller, string personName, CancellationToken ct);
    Task<IReadOnlyList<ContractSummary>> FindByCounterpartyAsync(ContractCallerContext caller, string counterparty, CancellationToken ct);
    Task<ContractPeriodResult?> FindContractForPeriodAsync(string consultantName, int year, int month, ContractCallerContext caller, CancellationToken ct);
    Task<ContractAnswer> AnswerAsync(ContractQuestion question, CancellationToken ct);
}

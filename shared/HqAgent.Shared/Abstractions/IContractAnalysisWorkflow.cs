using HqAgent.Shared.Models;

namespace HqAgent.Shared.Abstractions;

public interface IContractAnalysisWorkflow
{
    Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct = default);
}

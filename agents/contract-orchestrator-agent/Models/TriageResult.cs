using System.Text.Json.Serialization;

namespace ContractOrchestratorAgent.Models;

/// <summary>
/// Output from the Haiku triage step — document type and confidence only.
/// </summary>
public record TriageResult(
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("confidence")]   double Confidence
);

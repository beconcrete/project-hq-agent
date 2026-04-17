using System.Text.Json.Serialization;

namespace ContractOrchestratorAgent.Models;

/// <summary>
/// Structured contract fields extracted by the Sonnet or Opus step.
/// All optional fields use null when the contract does not contain them.
/// </summary>
public record ExtractionResult(
    [property: JsonPropertyName("documentType")]    string   DocumentType,
    [property: JsonPropertyName("parties")]         string[] Parties,
    [property: JsonPropertyName("effectiveDate")]   string?  EffectiveDate,
    [property: JsonPropertyName("expiryDate")]      string?  ExpiryDate,
    [property: JsonPropertyName("noticePeriodDays")]int?     NoticePeriodDays,
    [property: JsonPropertyName("governingLaw")]    string?  GoverningLaw,
    [property: JsonPropertyName("keyObligations")]  string[] KeyObligations,
    [property: JsonPropertyName("autoRenewal")]     bool     AutoRenewal,
    [property: JsonPropertyName("riskFlags")]       string[] RiskFlags,
    [property: JsonPropertyName("confidence")]      double   Confidence
);

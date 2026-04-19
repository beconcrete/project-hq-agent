using System.Text.Json.Serialization;

namespace HqAgent.Functions.Models;

public record ExtractionResult(
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("parties")] string[] Parties,
    [property: JsonPropertyName("effectiveDate")] string? EffectiveDate,
    [property: JsonPropertyName("expiryDate")] string? ExpiryDate,
    [property: JsonPropertyName("noticePeriod")] string? NoticePeriod,
    [property: JsonPropertyName("governingLaw")] string? GoverningLaw,
    [property: JsonPropertyName("keyObligations")] string[] KeyObligations,
    [property: JsonPropertyName("autoRenewal")] bool AutoRenewal,
    [property: JsonPropertyName("riskFlags")] string[] RiskFlags,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("modelUsed")] string? ModelUsed
);

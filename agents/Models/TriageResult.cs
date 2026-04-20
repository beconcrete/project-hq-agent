using System.Text.Json.Serialization;

namespace HqAgent.Agents.Models;

public record TriageResult(
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("confidence")]   double Confidence
);

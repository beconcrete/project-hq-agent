using System.Text.Json;
using System.Text.Json.Serialization;

namespace HqAgent.Shared.Models;

/// <summary>
/// Result of the two-step contract analysis pipeline (triage + extraction).
/// ExtractedFields is an open map — the model decides which fields are relevant
/// for the specific document type rather than filling a predetermined schema.
/// </summary>
public record ExtractionResult(
    [property: JsonPropertyName("documentType")]         string                            DocumentType,
    [property: JsonPropertyName("triageConfidence")]     double                            TriageConfidence,
    [property: JsonPropertyName("extractedFields")]      Dictionary<string, JsonElement>?  ExtractedFields,
    [property: JsonPropertyName("extractionConfidence")] double                            ExtractionConfidence,
    [property: JsonPropertyName("modelUsed")]            string                            ModelUsed,
    [property: JsonPropertyName("pendingReview")]        bool                              PendingReview
);

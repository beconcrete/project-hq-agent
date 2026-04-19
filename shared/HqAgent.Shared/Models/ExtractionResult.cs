using System.Text.Json;

namespace HqAgent.Shared.Models;

/// <summary>
/// Result of the two-step contract analysis pipeline (triage + extraction).
/// ExtractedFields is an open map — the model decides which fields are relevant
/// for the specific document type rather than filling a predetermined schema.
/// </summary>
public record ExtractionResult(
    string                            DocumentType,
    double                            TriageConfidence,
    Dictionary<string, JsonElement>?  ExtractedFields,
    double                            ExtractionConfidence,
    string                            ModelUsed,
    bool                              PendingReview
);

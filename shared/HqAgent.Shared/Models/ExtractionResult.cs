namespace HqAgent.Shared.Models;

/// <summary>
/// Result of the two-step contract analysis pipeline (triage + extraction).
/// ExtractedFields is the raw JSON string returned by the extraction model —
/// the model decides which fields are relevant for the specific document type.
/// </summary>
public record ExtractionResult(
    string  DocumentType,
    double  TriageConfidence,
    string? ExtractedFields,
    double  ExtractionConfidence,
    string  ModelUsed,
    bool    PendingReview
);

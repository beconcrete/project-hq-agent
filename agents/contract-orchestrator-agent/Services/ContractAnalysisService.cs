using System.Text.Json;
using System.Text.Json.Serialization;
using ContractOrchestratorAgent.Models;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Models;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Two-step contract analysis pipeline:
///   1. Triage  — Haiku 4.5 classifies document type (free-text, no enum)
///   2. Extract — Sonnet 4.6 extracts fields the model deems relevant for this document
/// If triage confidence is below the threshold the contract is flagged for human review
/// and extraction is skipped — no automatic escalation to a more expensive model.
/// </summary>
public class ContractAnalysisService
{
    private readonly IAIModelClient _client;
    private readonly ILogger<ContractAnalysisService> _logger;

    private const string TriageModel     = "claude-haiku-4-5-20251001";
    private const string ExtractionModel = "claude-sonnet-4-6";
    private const double ReviewThreshold = 0.7;

    private const string TriageSystemPrompt = """
        You are a contract classification expert. Identify the type of legal document provided.
        Output a free-text document type (e.g. "Non-Disclosure Agreement", "Software Licence",
        "Framework Agreement", "Public Sector Procurement Contract"). Be descriptive and precise.
        Set confidence to reflect how certain you are about the classification.
        """;

    private const string ExtractionSystemPrompt = """
        You are a contract analysis expert. Extract the key information from the legal document.

        Rules:
        - Decide which fields are relevant for this specific document — do not use a fixed schema.
        - Dates must be ISO 8601 (YYYY-MM-DD). Omit if not present.
        - Notice periods must be expressed as integer days (e.g. "1 month" = 30, "2 weeks" = 14).
        - Include parties, key obligations, risk flags (clauses unfavourable to either party such as
          unilateral termination, unlimited liability, automatic IP assignment, short notice periods).
        - Omit fields that are not present or not applicable for this document type.
        - Set confidence to reflect overall extraction quality (0.0 = guessing, 1.0 = certain).
        """;

    // ── Tool schemas ─────────────────────────────────────────────────────────

    private static readonly object TriageSchema = new
    {
        type = "object",
        properties = new
        {
            documentType = new
            {
                type        = "string",
                description = "Document type as a free-text description (e.g. 'Non-Disclosure Agreement', 'Software Licence', 'Framework Agreement', 'Public Sector Procurement Contract')",
            },
            confidence = new
            {
                type        = "number",
                description = "Classification confidence from 0.0 to 1.0",
            },
        },
        required = new[] { "documentType", "confidence" },
    };

    private static readonly object ExtractionSchema = new
    {
        type = "object",
        properties = new
        {
            fields = new
            {
                type                 = "object",
                additionalProperties = true,
                description          = "Key-value pairs of fields relevant to this specific contract. Choose field names that best describe this document. Common fields: parties (array), effectiveDate (ISO 8601), expiryDate (ISO 8601), noticePeriodDays (integer), governingLaw, keyObligations (array), autoRenewal (boolean), riskFlags (array) — include whatever is most relevant for this document type.",
            },
            confidence = new
            {
                type        = "number",
                description = "Extraction confidence from 0.0 to 1.0",
            },
        },
        required = new[] { "fields", "confidence" },
    };

    public ContractAnalysisService(IAIModelClient client, ILogger<ContractAnalysisService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ExtractionResult> AnalyzeAsync(
        byte[]            contractBytes,
        string            mediaType,
        CancellationToken ct = default)
    {
        // ── Step 1: Triage ───────────────────────────────────────────────────
        _logger.LogInformation("Triaging with {Model}", TriageModel);
        var triage = await _client.InvokeToolAsync<TriageResult>(
            model           : TriageModel,
            systemPrompt    : TriageSystemPrompt,
            documentBytes   : contractBytes,
            mediaType       : mediaType,
            toolName        : "classify_document",
            toolDescription : "Classify the type of legal document",
            toolInputSchema : TriageSchema,
            maxTokens       : 256,
            ct              : ct);

        _logger.LogInformation("Triage: {DocType} (confidence {Confidence:P0})",
            triage.DocumentType, triage.Confidence);

        // ── If triage confidence is low, flag for human review ────────────────
        if (triage.Confidence < ReviewThreshold)
        {
            _logger.LogWarning(
                "Triage confidence {C:P0} < {T:P0} — flagging for human review, skipping extraction",
                triage.Confidence, ReviewThreshold);

            return new ExtractionResult(
                DocumentType         : triage.DocumentType,
                TriageConfidence     : triage.Confidence,
                ExtractedFields      : null,
                ExtractionConfidence : 0,
                ModelUsed            : TriageModel,
                PendingReview        : true);
        }

        // ── Step 2: Extraction ───────────────────────────────────────────────
        _logger.LogInformation("Extracting with {Model}", ExtractionModel);
        var toolOutput = await _client.InvokeToolAsync<ExtractionToolOutput>(
            model           : ExtractionModel,
            systemPrompt    : ExtractionSystemPrompt,
            documentBytes   : contractBytes,
            mediaType       : mediaType,
            toolName        : "extract_contract_fields",
            toolDescription : "Extract relevant fields from a legal contract",
            toolInputSchema : ExtractionSchema,
            maxTokens       : 2048,
            ct              : ct);

        var extractedFields = new Dictionary<string, JsonElement>();
        foreach (var prop in toolOutput.Fields.EnumerateObject())
            extractedFields[prop.Name] = prop.Value.Clone();

        _logger.LogInformation("Extraction complete — confidence {C:P0}", toolOutput.Confidence);

        return new ExtractionResult(
            DocumentType         : triage.DocumentType,
            TriageConfidence     : triage.Confidence,
            ExtractedFields      : extractedFields,
            ExtractionConfidence : toolOutput.Confidence,
            ModelUsed            : ExtractionModel,
            PendingReview        : false);
    }

    private record ExtractionToolOutput(
        [property: JsonPropertyName("fields")]     JsonElement Fields,
        [property: JsonPropertyName("confidence")] double      Confidence
    );
}

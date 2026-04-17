using ContractOrchestratorAgent.Models;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Orchestrates the two-step Claude pipeline:
///   1. Triage  — Haiku 4.5 classifies document type
///   2. Extract — Sonnet 4.6 extracts structured fields; escalates to Opus 4.6 if confidence &lt; 0.7
/// </summary>
public class ClaudeService
{
    private readonly AnthropicHttpClient _claude;
    private readonly ILogger<ClaudeService> _logger;

    private const string TriageModel     = "claude-haiku-4-5-20251001";
    private const string ExtractionModel = "claude-sonnet-4-6";
    private const string EscalationModel = "claude-opus-4-6";
    private const double EscalationThreshold = 0.7;

    // ── System prompts ───────────────────────────────────────────────────────
    // Both are marked ephemeral — Anthropic caches them after the first request.

    private const string TriageSystemPrompt = """
        You are a contract classification expert. Identify the type of legal document provided.
        Be conservative — if you are not confident, use "unknown".
        """;

    private const string ExtractionSystemPrompt = """
        You are a contract analysis expert. Extract key structured information from the legal document.

        Rules:
        - Dates must be ISO 8601 (YYYY-MM-DD). Omit if not present.
        - Notice periods must be converted to integer days (e.g. "1 month" = 30, "2 weeks" = 14).
        - Risk flags: identify clauses unfavourable to either party such as unilateral termination,
          unlimited liability, automatic IP assignment, or unusually short notice periods.
        - If a field is absent from the document, omit it (null / empty default will be applied).
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
                @enum       = new[] { "nda", "msa", "loi", "consulting_assignment", "unknown" },
                description = "Type of legal document",
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
            documentType    = new { type = "string",  description = "Contract type" },
            parties         = new { type = "array",   items = new { type = "string" }, description = "All named parties" },
            effectiveDate   = new { type = "string",  description = "Effective date, YYYY-MM-DD" },
            expiryDate      = new { type = "string",  description = "Expiry or end date, YYYY-MM-DD" },
            noticePeriodDays= new { type = "integer", description = "Notice period in days" },
            governingLaw    = new { type = "string",  description = "Governing law jurisdiction" },
            keyObligations  = new { type = "array",   items = new { type = "string" }, description = "Key obligations" },
            autoRenewal     = new { type = "boolean", description = "Whether the contract auto-renews" },
            riskFlags       = new { type = "array",   items = new { type = "string" }, description = "Potentially risky clauses" },
            confidence      = new { type = "number",  description = "Overall extraction confidence 0.0–1.0" },
        },
        required = new[] { "documentType", "parties", "autoRenewal", "riskFlags", "keyObligations", "confidence" },
    };

    public ClaudeService(AnthropicHttpClient claude, ILogger<ClaudeService> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    /// <summary>
    /// Runs triage then extraction. Returns the extraction result and the model that produced it.
    /// </summary>
    public async Task<(ExtractionResult Result, string ModelUsed)> ExtractAsync(
        byte[]            contractBytes,
        string            mediaType,
        CancellationToken ct = default)
    {
        // ── Step 1: Triage ───────────────────────────────────────────────────
        _logger.LogInformation("Triaging with {Model}", TriageModel);
        var triage = await _claude.InvokeToolAsync<TriageResult>(
            model            : TriageModel,
            systemPrompt     : TriageSystemPrompt,
            documentBytes    : contractBytes,
            mediaType        : mediaType,
            toolName         : "classify_document",
            toolDescription  : "Classify the type of legal document",
            toolInputSchema  : TriageSchema,
            maxTokens        : 256,
            ct               : ct);

        _logger.LogInformation("Triage: {DocType} (confidence {Confidence:P0})",
            triage.DocumentType, triage.Confidence);

        // ── Step 2: Extraction ───────────────────────────────────────────────
        var (result, modelUsed) = await RunExtractionAsync(contractBytes, mediaType, ExtractionModel, ct);

        // ── Step 3: Escalate to Opus if confidence is too low ────────────────
        if (result.Confidence < EscalationThreshold)
        {
            _logger.LogWarning(
                "Sonnet confidence {C:P0} < {T:P0} — escalating to {Model}",
                result.Confidence, EscalationThreshold, EscalationModel);

            (result, modelUsed) = await RunExtractionAsync(contractBytes, mediaType, EscalationModel, ct);
        }

        return (result, modelUsed);
    }

    private async Task<(ExtractionResult, string)> RunExtractionAsync(
        byte[] contractBytes, string mediaType, string model, CancellationToken ct)
    {
        var result = await _claude.InvokeToolAsync<ExtractionResult>(
            model           : model,
            systemPrompt    : ExtractionSystemPrompt,
            documentBytes   : contractBytes,
            mediaType       : mediaType,
            toolName        : "extract_contract_fields",
            toolDescription : "Extract structured fields from a legal contract",
            toolInputSchema : ExtractionSchema,
            maxTokens       : 2048,
            ct              : ct);

        return (result, model);
    }
}

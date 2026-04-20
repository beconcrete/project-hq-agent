using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Contract analysis pipeline using direct Anthropic tool-use calls — no MAF handoff.
/// Two explicit steps: triage (Haiku) then extraction (Sonnet).
/// Used when AI_PROVIDER=anthropic.
/// </summary>
public class AnthropicContractWorkflow : IContractAnalysisWorkflow
{
    private readonly BlobStorageService _blobs;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;
    private readonly ILogger<AnthropicContractWorkflow> _logger;

    private const string TriageModel     = "claude-haiku-4-5-20251001";
    private const string ExtractionModel = "claude-sonnet-4-6";
    private const double ReviewThreshold = 0.7;

    private const string MessagesUrl      = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string BetaHeaders      = "pdfs-2024-09-25,prompt-caching-2024-07-31";

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

    private static readonly object TriageSchema = new
    {
        type = "object",
        properties = new
        {
            documentType = new { type = "string", description = "Free-text document type (e.g. 'Non-Disclosure Agreement')" },
            confidence   = new { type = "number", description = "Classification confidence 0.0–1.0" },
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
                description          = "Key-value pairs of fields relevant to this contract. Common fields: parties (array), effectiveDate, expiryDate, noticePeriodDays, governingLaw, keyObligations (array), autoRenewal, riskFlags (array).",
            },
            confidence = new { type = "number", description = "Extraction confidence 0.0–1.0" },
        },
        required = new[] { "fields", "confidence" },
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AnthropicContractWorkflow(
        BlobStorageService blobs,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _blobs       = blobs;
        _httpFactory = httpFactory;
        _logger      = loggerFactory.CreateLogger<AnthropicContractWorkflow>();
        _apiKey      = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured for Anthropic provider");
    }

    public async Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName}", msg.CorrelationId, msg.BlobName);

        var (bytes, contentType) = await _blobs.DownloadAsync(msg.ContainerName, msg.BlobName, ct);
        var mediaType = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";

        _logger.LogInformation("Triaging with {Model}", TriageModel);
        var triage = await InvokeToolAsync<TriageResult>(
            model: TriageModel, systemPrompt: TriageSystemPrompt,
            documentBytes: bytes, mediaType: mediaType,
            toolName: "classify_document", toolDescription: "Classify the type of legal document",
            toolInputSchema: TriageSchema, maxTokens: 256, ct: ct);

        _logger.LogInformation("Triage: {DocType} (confidence {Confidence:P0})", triage.DocumentType, triage.Confidence);

        if (triage.Confidence < ReviewThreshold)
        {
            _logger.LogWarning("Triage confidence {C:P0} < {T:P0} — flagging for review", triage.Confidence, ReviewThreshold);
            return new ExtractionResult(triage.DocumentType, triage.Confidence, null, 0, TriageModel, true);
        }

        _logger.LogInformation("Extracting with {Model}", ExtractionModel);
        var toolOutput = await InvokeToolAsync<ExtractionToolOutput>(
            model: ExtractionModel, systemPrompt: ExtractionSystemPrompt,
            documentBytes: bytes, mediaType: mediaType,
            toolName: "extract_contract_fields", toolDescription: "Extract relevant fields from a legal contract",
            toolInputSchema: ExtractionSchema, maxTokens: 2048, ct: ct);

        var extractedFields = toolOutput.Fields.GetRawText();

        _logger.LogInformation("Extraction complete — confidence {C:P0}", toolOutput.Confidence);

        return new ExtractionResult(triage.DocumentType, triage.Confidence, extractedFields, toolOutput.Confidence, ExtractionModel, false);
    }

    private async Task<T> InvokeToolAsync<T>(
        string model, string systemPrompt, byte[] documentBytes, string mediaType,
        string toolName, string toolDescription, object toolInputSchema,
        int maxTokens, CancellationToken ct)
    {
        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            system = new[] { new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } } },
            tools  = new[] { new { name = toolName, description = toolDescription, input_schema = toolInputSchema } },
            tool_choice = new { type = "tool", name = toolName },
            messages = new[]
            {
                new
                {
                    role    = "user",
                    content = new[] { new { type = "document", source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(documentBytes) } } }
                }
            },
        };

        var json    = JsonSerializer.Serialize(requestBody, SerializeOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        req.Headers.Add("x-api-key",        _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Headers.Add("anthropic-beta",    BetaHeaders);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Anthropic API error {Status}: {Body}", resp.StatusCode, err);
            throw new HttpRequestException($"Anthropic API returned {(int)resp.StatusCode}: {err}");
        }

        var responseJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc    = JsonDocument.Parse(responseJson);

        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_use")
            {
                var inputJson = block.GetProperty("input").GetRawText();
                return JsonSerializer.Deserialize<T>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException($"Failed to deserialise tool input for '{toolName}'");
            }
        }

        throw new InvalidOperationException($"Anthropic did not return a tool_use block for '{toolName}'");
    }

    private record TriageResult(
        [property: JsonPropertyName("documentType")] string DocumentType,
        [property: JsonPropertyName("confidence")]   double Confidence);

    private record ExtractionToolOutput(
        [property: JsonPropertyName("fields")]     JsonElement Fields,
        [property: JsonPropertyName("confidence")] double      Confidence);
}

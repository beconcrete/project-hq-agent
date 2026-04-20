using System.Text;
using System.Text.Json;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Contract analysis pipeline using direct sequential OpenAI calls.
/// Step 1: Extract PDF text (gpt-4.1-mini, file content type).
/// Step 2: Triage — classify document type + confidence (gpt-4.1-mini).
/// Step 3: Extract fields (gpt-4.1) if triage confidence >= 0.7.
/// </summary>
public class OpenAIContractWorkflow : IContractAnalysisWorkflow
{
    private readonly BlobStorageService _blobs;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;
    private readonly ILogger<OpenAIContractWorkflow> _logger;

    private const string TriageModel     = "gpt-4.1-mini";
    private const string ExtractionModel = "gpt-4.1";
    private const double ReviewThreshold = 0.7;
    private const string CompletionsUrl  = "https://api.openai.com/v1/chat/completions";

    private const string TriageSystemPrompt = """
        You are a contract classification specialist.
        Identify the type of legal document with a free-text description
        (e.g. "Non-Disclosure Agreement", "Software Licence", "Framework Agreement",
        "Public Sector Procurement Contract"). Set confidence from 0.0 to 1.0.
        Output ONLY JSON, no markdown, no code fences:
        {"documentType": "...", "confidence": 0.0}
        """;

    private const string ExtractionSystemPrompt = """
        You are a contract analyst. Extract the fields that are relevant for this specific
        document type — do not use a fixed schema.
        Common fields: parties (array), effectiveDate (ISO 8601), expiryDate (ISO 8601),
        noticePeriodDays (integer), governingLaw, keyObligations (array), autoRenewal (boolean),
        riskFlags (array) — include whatever is most relevant for this document.
        Output ONLY this JSON, no markdown, no code fences:
        {
          "documentType": "<from triage>",
          "triageConfidence": <from triage>,
          "extractedFields": { <key-value fields> },
          "extractionConfidence": <0.0-1.0>,
          "modelUsed": "gpt-4.1",
          "pendingReview": false
        }
        """;

    public OpenAIContractWorkflow(
        BlobStorageService blobs,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _blobs    = blobs;
        _httpFactory = httpFactory;
        _logger   = loggerFactory.CreateLogger<OpenAIContractWorkflow>();
        _apiKey   = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
    }

    public async Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName}", msg.CorrelationId, msg.BlobName);

        var (bytes, contentType) = await _blobs.DownloadAsync(msg.ContainerName, msg.BlobName, ct);

        // Step 1: extract text
        string text;
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            msg.BlobName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            text = await ExtractPdfTextAsync(bytes, ct);
        else
            text = Encoding.UTF8.GetString(bytes);

        // Step 2: triage
        _logger.LogInformation("Triaging with {Model}", TriageModel);
        var triageJson = await ChatAsync(TriageModel, TriageSystemPrompt, text, 256, ct);
        _logger.LogInformation("Triage raw: {Raw}", triageJson);

        using var triageDoc  = JsonDocument.Parse(ExtractLastJson(triageJson));
        var triageRoot       = triageDoc.RootElement;
        var documentType     = triageRoot.TryGetProperty("documentType", out var dt) ? dt.GetString() ?? "" : "";
        var triageConfidence = triageRoot.TryGetProperty("confidence",   out var tc) ? tc.GetDouble()       : 0;

        _logger.LogInformation("Triage: {DocType} confidence {Confidence:P0}", documentType, triageConfidence);

        if (triageConfidence < ReviewThreshold)
        {
            _logger.LogWarning("Low confidence {C:P0} — flagging for review", triageConfidence);
            return new ExtractionResult(documentType, triageConfidence, null, 0, TriageModel, true);
        }

        // Step 3: extraction
        _logger.LogInformation("Extracting with {Model}", ExtractionModel);
        var userPrompt    = $"Triage result: {triageJson}\n\nDocument:\n{text}";
        var extractionJson = await ChatAsync(ExtractionModel, ExtractionSystemPrompt, userPrompt, 2048, ct);
        _logger.LogInformation("Extraction raw: {Raw}", extractionJson[..Math.Min(500, extractionJson.Length)]);

        return ParseExtraction(extractionJson);
    }

    private async Task<string> ChatAsync(
        string model, string systemPrompt, string userContent, int maxTokens, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userContent  },
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, CompletionsUrl);
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI {Model} error {Status}: {Body}", model, resp.StatusCode, err);
            throw new HttpRequestException($"OpenAI {model} returned {(int)resp.StatusCode}: {err}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private async Task<string> ExtractPdfTextAsync(byte[] pdfBytes, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model      = TriageModel,
            max_tokens = 8192,
            messages = new[]
            {
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "file",
                            file = new
                            {
                                filename  = "document.pdf",
                                file_data = $"data:application/pdf;base64,{Convert.ToBase64String(pdfBytes)}",
                            }
                        },
                        new { type = "text", text = "Extract all text content from this document verbatim, preserving structure. No commentary." }
                    }
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, CompletionsUrl);
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI PDF extraction error {Status}: {Body}", resp.StatusCode, err);
            throw new HttpRequestException($"OpenAI returned {(int)resp.StatusCode}: {err}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var text = JsonDocument.Parse(json).RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        _logger.LogInformation("PDF text extracted: {CharCount} chars", text.Length);
        return text;
    }

    private static ExtractionResult ParseExtraction(string raw)
    {
        var json = ExtractLastJson(raw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string  documentType         = root.TryGetProperty("documentType",         out var dt) ? dt.GetString()  ?? "" : "";
        double  triageConfidence     = root.TryGetProperty("triageConfidence",     out var tc) ? tc.GetDouble()       : 0;
        double  extractionConfidence = root.TryGetProperty("extractionConfidence", out var ec) ? ec.GetDouble()       : 0;
        string  modelUsed            = root.TryGetProperty("modelUsed",            out var mu) ? mu.GetString()  ?? "" : "";
        bool    pendingReview        = root.TryGetProperty("pendingReview",        out var pr) ? pr.GetBoolean()      : false;
        string? extractedFields      = root.TryGetProperty("extractedFields",      out var ef) ? ef.GetRawText()      : null;

        return new ExtractionResult(documentType, triageConfidence, extractedFields, extractionConfidence, modelUsed, pendingReview);
    }

    private static string ExtractLastJson(string text)
    {
        string? last = null;
        var searchFrom = 0;
        while (true)
        {
            var start = text.IndexOf('{', searchFrom);
            if (start == -1) break;
            var json = TryExtractJsonAt(text, start);
            if (json != null) last = json;
            searchFrom = start + 1;
        }
        return last ?? throw new InvalidOperationException(
            $"No JSON in response. Preview: {text[..Math.Min(300, text.Length)]}");
    }

    private static string? TryExtractJsonAt(string text, int start)
    {
        int depth = 0; bool inString = false; bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped)           { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true;  continue; }
            if (c == '"')          { inString = !inString; continue; }
            if (inString)          continue;
            if (c == '{')          depth++;
            else if (c == '}' && --depth == 0) return text[start..(i + 1)];
        }
        return null;
    }
}

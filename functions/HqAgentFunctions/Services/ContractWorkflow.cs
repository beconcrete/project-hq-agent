using Anthropic;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Anthropic;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace HqAgent.Functions.Services;

public class ContractWorkflow
{
    private readonly BlobStorageService _blobs;
    private readonly IAnthropicClient _anthropic;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _anthropicApiKey;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ContractWorkflow> _logger;

    private const string TriageInstructions = """
        You are a contract classification specialist.
        Identify the type of legal document with a free-text description
        (e.g. "Non-Disclosure Agreement", "Software Licence", "Framework Agreement",
        "Public Sector Procurement Contract"). Set confidence from 0.0 to 1.0.

        If confidence is below 0.7, output ONLY this JSON object and stop — do NOT transfer to extraction:
        {
          "documentType": "<your classification>",
          "triageConfidence": <confidence>,
          "extractedFields": null,
          "extractionConfidence": 0.0,
          "modelUsed": "claude-haiku-4-5-20251001",
          "pendingReview": true
        }

        Otherwise, transfer to the extraction agent immediately. Do not output any text.
        """;

    private const string ExtractionInstructions = """
        You are a contract analyst. The triage agent has classified this document.
        Read the conversation history for the document type and triage confidence.

        Extract the fields that are relevant for this specific document type — do not use a fixed schema.
        Common fields: parties (array), effectiveDate (ISO 8601), expiryDate (ISO 8601),
        noticePeriodDays (integer), governingLaw, keyObligations (array), autoRenewal (boolean),
        riskFlags (array) — include whatever is most relevant for this document.

        Output ONLY this JSON object, no markdown, no code fences, no other text:
        {
          "documentType": "<document type from triage>",
          "triageConfidence": <triage confidence from chat history>,
          "extractedFields": { <your extracted key-value fields> },
          "extractionConfidence": <your confidence 0.0-1.0>,
          "modelUsed": "claude-sonnet-4-6",
          "pendingReview": false
        }
        """;

    public ContractWorkflow(
        IAnthropicClient anthropic,
        BlobStorageService blobs,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _anthropic = anthropic;
        _blobs = blobs;
        _httpFactory = httpFactory;
        _loggerFactory = loggerFactory;
        _anthropicApiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");
        _logger = loggerFactory.CreateLogger<ContractWorkflow>();
    }

    public async Task<ExtractionResult> RunAsync(
        ContractMessage msg,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName}", msg.CorrelationId, msg.BlobName);

        var userMessage = await BuildMessageAsync(msg, ct);

        // Build agents and workflow fresh per run — gives each job isolated in-memory history
        // so the extraction agent sees triage output without cross-job contamination.
        var triageAgent = _anthropic.AsAIAgent(
            model: "claude-haiku-4-5-20251001",
            instructions: TriageInstructions,
            name: "triage",
            description: "Classifies the contract document type; flags for human review if confidence is below 0.7",
            loggerFactory: _loggerFactory);

        var extractionAgent = _anthropic.AsAIAgent(
            model: "claude-sonnet-4-6",
            instructions: ExtractionInstructions,
            name: "extraction",
            description: "Extracts open-ended contract fields for any document type",
            loggerFactory: _loggerFactory);

#pragma warning disable MAAIW001
        var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
            .WithHandoff(triageAgent, extractionAgent)
            .Build();
#pragma warning restore MAAIW001

        await using var run = await InProcessExecution.OpenStreamingAsync(
            workflow, sessionId: msg.CorrelationId);

        await run.TrySendMessageAsync(userMessage);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        var sb = new StringBuilder();
        await foreach (var evt in run.WatchStreamAsync().WithCancellation(ct))
        {
            if (evt is AgentResponseUpdateEvent update)
                sb.Append(update.Update.Text ?? "");
            else if (evt is WorkflowErrorEvent err)
                throw err.Exception ?? new InvalidOperationException("Workflow error during contract processing");
            else if (evt is WorkflowOutputEvent)
                break;
        }

        _logger.LogInformation("Workflow complete for {CorrelationId}", msg.CorrelationId);

        return ParseExtraction(sb.ToString());
    }

    private async Task<ChatMessage> BuildMessageAsync(ContractMessage msg, CancellationToken ct)
    {
        var (bytes, contentType) = await _blobs.DownloadAsync(msg.ContainerName, msg.BlobName, ct);

        string text;
        if (IsPdf(contentType, msg.BlobName))
            text = await ExtractPdfTextAsync(bytes, ct);
        else
            text = Encoding.UTF8.GetString(bytes);

        return new ChatMessage(ChatRole.User, [new TextContent(text)]);
    }

    private async Task<string> ExtractPdfTextAsync(byte[] pdfBytes, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 8192,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "document", source = new { type = "base64", media_type = "application/pdf", data = Convert.ToBase64String(pdfBytes) } },
                        new { type = "text", text = "Extract all text content from this document. Output the full text verbatim, preserving structure. No commentary." }
                    }
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", _anthropicApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Add("anthropic-beta", "pdfs-2024-09-25");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var text = JsonDocument.Parse(json).RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        _logger.LogInformation("PDF text extracted: {CharCount} chars", text.Length);
        return text;
    }

    private static bool IsPdf(string contentType, string blobName) =>
        contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
        blobName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static ExtractionResult ParseExtraction(string raw)
    {
        var json = ExtractLastJson(raw);
        return JsonSerializer.Deserialize<ExtractionResult>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Null deserialization result");
    }

    // Finds the last complete top-level JSON object in the text.
    // Triage may emit JSON before handing off to extraction; we always want the
    // extraction agent's output, which appears last in the combined response stream.
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
            $"No JSON object in workflow response. Preview: {text[..Math.Min(300, text.Length)]}");
    }

    private static string? TryExtractJsonAt(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
                return text[start..(i + 1)];
        }

        return null;
    }
}

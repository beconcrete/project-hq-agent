using System.Text;
using System.Text.Json;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Contract analysis pipeline using OpenAI + MAF handoff workflow.
/// OpenAI supports assistant message prefill, so the MAF triage → extraction
/// handoff works correctly (unlike Anthropic which rejects it).
/// </summary>
public class OpenAIContractWorkflow : IContractAnalysisWorkflow
{
    private readonly BlobStorageService _blobs;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OpenAIContractWorkflow> _logger;

    private readonly IChatClient _triageChatClient;
    private readonly IChatClient _extractionChatClient;

    private const string TriageModel     = "gpt-4.1-mini";
    private const string ExtractionModel = "gpt-4.1";

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
          "modelUsed": "gpt-4.1-mini",
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
        _blobs         = blobs;
        _httpFactory   = httpFactory;
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<OpenAIContractWorkflow>();

        _apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        var openAiClient = new OpenAIClient(_apiKey);
        _triageChatClient     = openAiClient.GetChatClient(TriageModel).AsIChatClient();
        _extractionChatClient = openAiClient.GetChatClient(ExtractionModel).AsIChatClient();
    }

    public async Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName} — apiKeyLength:{KeyLen} apiKeyPrefix:{Prefix}",
            msg.CorrelationId, msg.BlobName, _apiKey.Length, _apiKey.Length >= 7 ? _apiKey[..7] : "(short)");

        var userMessage = await BuildMessageAsync(msg, ct);

        var triageAgent = new ChatClientAgent(_triageChatClient, new ChatClientAgentOptions
        {
            Name        = "triage",
            Description = "Classifies the contract document type; flags for human review if confidence is below 0.7",
            ChatOptions = new ChatOptions { Instructions = TriageInstructions },
        });

        var extractionAgent = new ChatClientAgent(_extractionChatClient, new ChatClientAgentOptions
        {
            Name        = "extraction",
            Description = "Extracts open-ended contract fields for any document type",
            ChatOptions = new ChatOptions { Instructions = ExtractionInstructions },
        });

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

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
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

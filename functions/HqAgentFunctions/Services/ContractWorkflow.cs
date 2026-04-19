using Anthropic;
using Azure.Storage.Blobs;
using HqAgent.Functions.Models;
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
    private readonly BlobServiceClient _blobs;
    private readonly AIAgent _workflowAgent;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _anthropicApiKey;
    private readonly ILogger<ContractWorkflow> _logger;

    private const string TriageInstructions = """
        You are a contract classification specialist.
        Classify the document type from: NDA, MSA, LOI, ASSIGNMENT, SERVICE_AGREEMENT, EMPLOYMENT, LEASE, OTHER.
        Transfer to the extraction agent immediately. Do not output any text.
        """;

    private const string ExtractionInstructions = """
        You are a contract analyst. Extract structured fields from the contract and return a JSON object.
        Required fields:
        {
          "documentType": "string — NDA/MSA/LOI/ASSIGNMENT/SERVICE_AGREEMENT/EMPLOYMENT/LEASE/OTHER",
          "parties": ["string"],
          "effectiveDate": "ISO 8601 date string or null",
          "expiryDate": "ISO 8601 date string or null",
          "noticePeriod": "string or null",
          "governingLaw": "string or null",
          "keyObligations": ["string"],
          "autoRenewal": true or false,
          "riskFlags": ["string"],
          "confidence": number from 0.0 to 1.0,
          "modelUsed": "claude-sonnet-4-6"
        }
        Return only valid JSON — no markdown, no code fences, no explanation.
        If your confidence is below 0.7, transfer to the escalation agent for a more thorough analysis.
        """;

    private const string EscalationInstructions = """
        You are a senior contract analyst. The initial extraction had low confidence — perform careful, thorough analysis.
        Extract all fields from the contract and return a JSON object:
        {
          "documentType": "string — NDA/MSA/LOI/ASSIGNMENT/SERVICE_AGREEMENT/EMPLOYMENT/LEASE/OTHER",
          "parties": ["string"],
          "effectiveDate": "ISO 8601 date string or null",
          "expiryDate": "ISO 8601 date string or null",
          "noticePeriod": "string or null",
          "governingLaw": "string or null",
          "keyObligations": ["string"],
          "autoRenewal": true or false,
          "riskFlags": ["string"],
          "confidence": number from 0.0 to 1.0,
          "modelUsed": "claude-opus-4-6"
        }
        Return only valid JSON — no markdown, no code fences, no explanation.
        """;

    public ContractWorkflow(
        IAnthropicClient anthropic,
        BlobServiceClient blobs,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _blobs = blobs;
        _httpFactory = httpFactory;
        _anthropicApiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");
        _logger = loggerFactory.CreateLogger<ContractWorkflow>();

        var triageAgent = anthropic.AsAIAgent(
            model: "claude-haiku-4-5-20251001",
            instructions: TriageInstructions,
            name: "triage",
            description: "Classifies the contract document type",
            loggerFactory: loggerFactory
        );

        var extractionAgent = anthropic.AsAIAgent(
            model: "claude-sonnet-4-6",
            instructions: ExtractionInstructions,
            name: "extraction",
            description: "Extracts structured contract fields; escalates when confidence is below 0.7",
            loggerFactory: loggerFactory
        );

        var escalationAgent = anthropic.AsAIAgent(
            model: "claude-opus-4-6",
            instructions: EscalationInstructions,
            name: "escalation",
            description: "Performs careful re-extraction for low-confidence contracts",
            loggerFactory: loggerFactory
        );

#pragma warning disable MAAIW001
        var workflow = new HandoffWorkflowBuilder(triageAgent)
            .WithHandoff(triageAgent, extractionAgent)
            .WithHandoff(extractionAgent, escalationAgent)
            .Build();
#pragma warning restore MAAIW001

        _workflowAgent = workflow.AsAIAgent(
            id: "contract-workflow",
            name: "ContractWorkflow",
            description: "Processes contracts through triage, extraction, and optional escalation",
            executionEnvironment: InProcessExecution.Default,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: true
        );
    }

    public async Task<(ExtractionResult Extraction, string ModelUsed)> RunAsync(
        ContractMessage msg,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName}", msg.CorrelationId, msg.BlobName);

        var message = await BuildMessageAsync(msg, ct);
        var response = await _workflowAgent.RunAsync(message, cancellationToken: ct);
        var raw = response.Text ?? throw new InvalidOperationException("Workflow returned empty response");

        _logger.LogInformation("Workflow complete for {CorrelationId}", msg.CorrelationId);

        var extraction = ParseExtraction(raw);
        var modelUsed = extraction.ModelUsed ?? "claude-sonnet-4-6";
        return (extraction, modelUsed);
    }

    private async Task<ChatMessage> BuildMessageAsync(ContractMessage msg, CancellationToken ct)
    {
        var container = _blobs.GetBlobContainerClient(msg.ContainerName);
        var blob = container.GetBlobClient(msg.BlobName);
        var download = await blob.DownloadContentAsync(ct);
        var bytes = download.Value.Content.ToArray();
        var contentType = download.Value.Details.ContentType ?? "";

        _logger.LogInformation("Downloaded {BlobName}: {Size} bytes", msg.BlobName, bytes.Length);

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
        var json = ExtractJson(raw);
        return JsonSerializer.Deserialize<ExtractionResult>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Null deserialization result");
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start == -1)
            throw new InvalidOperationException($"No JSON object in workflow response. Preview: {text[..Math.Min(300, text.Length)]}");

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

        throw new InvalidOperationException($"No complete JSON object in workflow response. Preview: {text[..Math.Min(300, text.Length)]}");
    }
}

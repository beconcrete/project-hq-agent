using System.Text;
using System.Text.Json;
using HqAgent.Agents.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace HqAgent.Agents.Contract.Agents;

/// <summary>
/// Contract analysis pipeline using OpenAI + MAF handoff workflow.
/// OpenAI supports assistant message prefill, so the MAF triage → extraction
/// handoff works correctly (unlike Anthropic which rejects it).
/// </summary>
public class ContractOrchestratorAgent
{
    private readonly BlobStorageService _blobs;
    private readonly DocumentTextExtractor _textExtractor;
    private readonly string _apiKey;
    private readonly ILogger<ContractOrchestratorAgent> _logger;

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
        riskFlags (array), paymentAmount (number), paymentCurrency, paymentUnit, paymentType,
        paymentTerms — include whatever is most relevant for this document.

        For consulting agreements, prioritize: customer/client, supplier/vendor, consultantNames,
        assignmentTitle, assignmentDescription, assignmentStartDate, assignmentEndDate, workloadPercent,
        hourlyRate or dailyRate, currency, invoicingTerms, paymentTerms, customerContact,
        internalOwner, extensionOption, terminationTerms, ipOwnership, nonSolicitation.

        For NDAs, prioritize: parties, mutualOrOneWay, effectiveDate, expiryDate,
        confidentialityPeriod, survivalPeriod, purpose, permittedDisclosures,
        returnOrDestructionObligation, governingLaw, jurisdiction, signatories.

        For software licences, prioritize: customer/licensee, supplier/licensor, productName,
        licenceMetric, seatCount, licenceStartDate, licenceEndDate, renewalTerms,
        paymentAmount, paymentCurrency, paymentUnit, paymentType, supportTerms, auditRights.

        For service/customer agreements, prioritize: customer/client, supplier/vendor,
        serviceDescription, serviceStartDate, serviceEndDate, serviceLevels, renewalTerms,
        terminationTerms, paymentAmount, paymentCurrency, paymentUnit, paymentType, paymentTerms.

        For one-time engagements, prioritize: customer/client, supplier/vendor, eventDate,
        serviceDescription, peopleMentioned, paymentAmount, paymentCurrency, paymentUnit,
        paymentType, paymentTerms, cancellationTerms.

        Set pendingReview to true if the document type is ambiguous, extraction confidence is below 0.75,
        dates or parties conflict, payment facts conflict, or a core field for the document type is missing.

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

    public ContractOrchestratorAgent(
        BlobStorageService blobs,
        DocumentTextExtractor textExtractor,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _blobs       = blobs;
        _textExtractor = textExtractor;
        _logger      = loggerFactory.CreateLogger<ContractOrchestratorAgent>();

        _apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        var openAiClient = new OpenAIClient(_apiKey);
        _triageChatClient     = openAiClient.GetChatClient(TriageModel).AsIChatClient();
        _extractionChatClient = openAiClient.GetChatClient(ExtractionModel).AsIChatClient();
    }

    public async Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing contract {CorrelationId} — {BlobName}", msg.CorrelationId, msg.BlobName);

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

        var raw = sb.ToString();
        _logger.LogInformation("Workflow output for {CorrelationId}: {Raw}", msg.CorrelationId, raw);

        return ParseExtraction(raw);
    }

    private async Task<ChatMessage> BuildMessageAsync(ContractMessage msg, CancellationToken ct)
    {
        var (bytes, contentType) = await _blobs.DownloadAsync(msg.ContainerName, msg.BlobName, ct);
        var text = await _textExtractor.ExtractAsync(bytes, contentType, msg.BlobName, ct);

        return new ChatMessage(ChatRole.User, [new TextContent(text)]);
    }

    private static ExtractionResult ParseExtraction(string raw)
    {
        var json = ExtractOutermostJson(raw);
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

    private static string ExtractOutermostJson(string text)
    {
        var searchFrom = 0;
        while (true)
        {
            var start = text.IndexOf('{', searchFrom);
            if (start == -1) break;
            var candidate = TryExtractJsonAt(text, start);
            if (candidate != null)
            {
                try { JsonDocument.Parse(candidate); return candidate; }
                catch (JsonException) { }
            }
            searchFrom = start + 1;
        }
        throw new InvalidOperationException(
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

using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace HqAgent.Agents.Contract.Agents;

public class ContractChatAgent
{
    private const string MiniModel        = "gpt-4.1-mini";
    private const string FullModel        = "gpt-4.1";
    private const string ChatHistoryTable = "ContractChatHistory";
    private const int    MaxHistoryTurns  = 20;

    private const string SystemPromptText = """
        You are a contract analyst assistant. You have access to extracted contract fields and possibly the full contract document.

        Answer questions about this specific contract accurately and concisely.
        Only use information present in the provided data. Never hallucinate clause text or dates.
        If information is not available in the extracted fields, set needs_document to true.

        Always respond with valid JSON in exactly this format:
        {
          "answer": "your complete answer",
          "confidence": 0.0,
          "needs_document": false,
          "sources": ["extracted_fields"]
        }

        confidence: 0.0–1.0 (how certain you are).
        needs_document: true only when extracted fields lack the information needed.
        sources: use "extracted_fields", "original_document", or both.
        """;

    private readonly ChatClient          _miniClient;
    private readonly ChatClient          _fullClient;
    private readonly IHttpClientFactory  _httpFactory;
    private readonly string              _apiKey;
    private readonly TableServiceClient  _tableClient;
    private readonly BlobStorageService  _blobs;
    private readonly ILogger<ContractChatAgent> _logger;

    public ContractChatAgent(
        IHttpClientFactory         httpFactory,
        TableServiceClient         tableClient,
        BlobStorageService         blobs,
        IConfiguration             config,
        ILogger<ContractChatAgent> logger)
    {
        _httpFactory = httpFactory;
        _tableClient = tableClient;
        _blobs       = blobs;
        _logger      = logger;
        _apiKey      = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        var openAiClient = new OpenAIClient(_apiKey);
        _miniClient = openAiClient.GetChatClient(MiniModel);
        _fullClient = openAiClient.GetChatClient(FullModel);
    }

    public async Task<ChatResult> ChatAsync(
        ContractExtractionEntity entity,
        string sessionId,
        string message,
        CancellationToken ct)
    {
        var history    = await LoadHistoryAsync(sessionId, ct);
        var fieldsJson = string.IsNullOrEmpty(entity.Fields) ? "{}" : entity.Fields;

        var messages    = BuildMessages(history, message, documentText: null);
        var rawResponse = await CallOpenAIAsync(_miniClient, fieldsJson, messages, ct);
        var parsed      = ParseResponse(rawResponse);

        var sources   = parsed.Sources;
        var modelUsed = MiniModel;

        if (parsed.NeedsDocument)
        {
            _logger.LogInformation("Fetching document for {CorrelationId}", entity.RowKey);
            var docText  = await FetchDocumentTextAsync(entity, ct);
            messages    = BuildMessages(history, message, docText);
            var usesFull = parsed.Confidence < 0.5;
            rawResponse = await CallOpenAIAsync(usesFull ? _fullClient : _miniClient, fieldsJson, messages, ct);
            parsed      = ParseResponse(rawResponse);
            sources     = parsed.Sources.Contains("extracted_fields")
                ? ["extracted_fields", "original_document"]
                : ["original_document"];
            modelUsed   = usesFull ? FullModel : MiniModel;
        }
        else if (parsed.Confidence < 0.6)
        {
            _logger.LogInformation("Low confidence ({C:F2}) — escalating for {CorrelationId}", parsed.Confidence, entity.RowKey);
            rawResponse = await CallOpenAIAsync(_fullClient, fieldsJson, messages, ct);
            parsed      = ParseResponse(rawResponse);
            modelUsed   = FullModel;
        }

        await SaveTurnAsync(sessionId, "user",      message,       ct);
        await SaveTurnAsync(sessionId, "assistant", parsed.Answer, ct);

        return new ChatResult(parsed.Answer, parsed.Confidence, modelUsed, sources);
    }

    // ── OpenAI ────────────────────────────────────────────────────────────────

    private async Task<string> CallOpenAIAsync(
        ChatClient client, string fieldsJson,
        IList<ChatMessage> messages, CancellationToken ct)
    {
        var allMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(
                $"{SystemPromptText}\n\nExtracted contract fields:\n{fieldsJson}")
        };
        allMessages.AddRange(messages);

        var options    = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() };
        var completion = await client.CompleteChatAsync(allMessages, options, ct);
        return completion.Value.Content[0].Text;
    }

    private static List<ChatMessage> BuildMessages(
        IReadOnlyList<ChatTurnEntity> history,
        string userMessage,
        string? documentText)
    {
        var list = new List<ChatMessage>();
        foreach (var turn in history)
        {
            list.Add(turn.Role == "user"
                ? ChatMessage.CreateUserMessage(turn.Content)
                : ChatMessage.CreateAssistantMessage(turn.Content));
        }

        var content = documentText is not null
            ? $"Full contract text:\n{documentText}\n\nQuestion: {userMessage}"
            : userMessage;
        list.Add(ChatMessage.CreateUserMessage(content));
        return list;
    }

    private static ChatResponseParsed ParseResponse(string raw)
    {
        try
        {
            var start = raw.IndexOf('{'); var end = raw.LastIndexOf('}');
            var json  = start >= 0 && end > start ? raw[start..(end + 1)] : raw;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var answer     = root.TryGetProperty("answer",         out var a) ? a.GetString() ?? raw : raw;
            var confidence = root.TryGetProperty("confidence",     out var c) ? c.GetDouble()        : 0.5;
            var needsDoc   = root.TryGetProperty("needs_document", out var n) && n.GetBoolean();
            var sources    = new List<string> { "extracted_fields" };

            if (root.TryGetProperty("sources", out var s) && s.ValueKind == JsonValueKind.Array)
            {
                sources.Clear();
                foreach (var el in s.EnumerateArray())
                    if (el.GetString() is { } src) sources.Add(src);
            }

            return new ChatResponseParsed(answer, confidence, needsDoc, sources);
        }
        catch { return new ChatResponseParsed(raw, 0.5, false, ["extracted_fields"]); }
    }

    // ── Document fetch ────────────────────────────────────────────────────────

    private async Task<string?> FetchDocumentTextAsync(
        ContractExtractionEntity entity, CancellationToken ct)
    {
        try
        {
            var (bytes, contentType) = await _blobs.DownloadAsync("contracts", entity.BlobPath, ct);
            var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                || entity.BlobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            return isPdf
                ? await ExtractPdfTextAsync(bytes, ct)
                : Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch document for {BlobPath}", entity.BlobPath);
            return null;
        }
    }

    private async Task<string> ExtractPdfTextAsync(byte[] pdfBytes, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model      = MiniModel,
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

    // ── Chat history ──────────────────────────────────────────────────────────

    private async Task<List<ChatTurnEntity>> LoadHistoryAsync(
        string sessionId, CancellationToken ct)
    {
        var table   = _tableClient.GetTableClient(ChatHistoryTable);
        var results = new List<ChatTurnEntity>();
        try
        {
            await foreach (var e in table.QueryAsync<ChatTurnEntity>(
                filter: $"PartitionKey eq '{sessionId}'", cancellationToken: ct))
                results.Add(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        results.Sort((a, b) => string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal));
        return results.Count > MaxHistoryTurns ? results[^MaxHistoryTurns..] : results;
    }

    private async Task SaveTurnAsync(
        string sessionId, string role, string content, CancellationToken ct)
    {
        var table = _tableClient.GetTableClient(ChatHistoryTable);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new ChatTurnEntity
        {
            PartitionKey = sessionId,
            RowKey       = DateTime.UtcNow.Ticks.ToString("D20"),
            Role         = role,
            Content      = content,
        }, TableUpdateMode.Replace, ct);
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private record ChatResponseParsed(
        string Answer, double Confidence, bool NeedsDocument, List<string> Sources);
}

public record ChatResult(string Answer, double Confidence, string ModelUsed, List<string> Sources);

using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HqAgent.Api;

/// <summary>
/// Contract Q&amp;A endpoint.
/// POST /api/contract-chat — { correlationId, sessionId, message }
/// Fast path: answers from extracted Table Storage fields.
/// Fallback: fetches original blob when extracted data is insufficient.
/// Escalates from Sonnet to Opus when confidence is low.
/// </summary>
public class ContractChat
{
    private const string SonnetModel      = "claude-sonnet-4-6";
    private const string OpusModel        = "claude-opus-4-7";
    private const string ChatHistoryTable = "ContractChatHistory";
    private const int    MaxHistoryTurns  = 20; // 10 exchanges

    private const string SystemPromptText = """
        You are a contract analyst assistant. You have access to extracted contract fields and possibly the full contract document.

        Answer questions about this specific contract accurately and concisely.
        Only use information present in the provided data. Never hallucinate clause text or dates.
        If information is not available in the extracted fields, set needs_document to true.

        Always respond with valid JSON in exactly this format — no markdown, no code fences:
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

    private readonly IHttpClientFactory  _httpFactory;
    private readonly TableStorageService _table;
    private readonly TableServiceClient  _tableClient;
    private readonly BlobStorageService  _blobs;
    private readonly string              _appId;
    private readonly string              _anthropicKey;
    private readonly ILogger<ContractChat> _logger;

    public ContractChat(
        IHttpClientFactory  httpFactory,
        TableStorageService table,
        TableServiceClient  tableClient,
        BlobStorageService  blobs,
        IConfiguration      config,
        ILogger<ContractChat> logger)
    {
        _httpFactory  = httpFactory;
        _table        = table;
        _tableClient  = tableClient;
        _blobs        = blobs;
        _appId        = config["APP_ID"] ?? "hqagents";
        _anthropicKey = config["ANTHROPIC_API_KEY"] ?? "";
        _logger       = logger;
    }

    [Function("ContractChat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "contract-chat")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.User); }
        catch { return await Plain(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }
        if (!guard.Allowed) return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");

        ChatRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<ChatRequest>(req.Body); }
        catch { return await Plain(req, HttpStatusCode.BadRequest, "Invalid JSON body"); }

        if (body is null || string.IsNullOrWhiteSpace(body.CorrelationId)
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Message))
            return await Plain(req, HttpStatusCode.BadRequest, "correlationId, sessionId and message are required");

        var userId  = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";
        var isAdmin = guard.RoleIds.Contains(Roles.Admin);

        var entity = await _table.GetExtractionAsync(body.CorrelationId);
        if (entity is null)
            return await Plain(req, HttpStatusCode.NotFound, "Contract not found");
        if (!isAdmin && entity.UserId != userId)
            return await Plain(req, HttpStatusCode.Forbidden, "Forbidden");
        if (entity.Status is not ("completed" or "pending_review"))
            return await Plain(req, HttpStatusCode.Conflict, "Contract is not yet processed");

        // Load conversation history
        var history = await LoadHistoryAsync(body.SessionId, context.CancellationToken);

        // Build extracted fields context
        var fieldsJson = string.IsNullOrEmpty(entity.Fields) ? "{}" : entity.Fields;

        // First attempt — extracted fields only (Sonnet)
        var messages = BuildMessages(history, body.Message, document: null);
        var (rawResponse, _) = await CallAnthropicAsync(SonnetModel, fieldsJson, messages, context.CancellationToken);
        var parsed = ParseResponse(rawResponse);

        // Fallback — fetch original document if model asked for it
        var sources   = parsed.Sources;
        var modelUsed = SonnetModel;

        if (parsed.NeedsDocument)
        {
            _logger.LogInformation("Fetching document for {CorrelationId}", body.CorrelationId);
            var document = await FetchDocumentAsync(entity, context.CancellationToken);
            messages = BuildMessages(history, body.Message, document);
            var model = parsed.Confidence < 0.5 ? OpusModel : SonnetModel;
            (rawResponse, _) = await CallAnthropicAsync(model, fieldsJson, messages, context.CancellationToken);
            parsed    = ParseResponse(rawResponse);
            sources   = parsed.Sources.Contains("extracted_fields")
                ? ["extracted_fields", "original_document"]
                : ["original_document"];
            modelUsed = model;
        }
        else if (parsed.Confidence < 0.6)
        {
            _logger.LogInformation("Low confidence ({C:F2}) — escalating to Opus for {CorrelationId}", parsed.Confidence, body.CorrelationId);
            (rawResponse, _) = await CallAnthropicAsync(OpusModel, fieldsJson, messages, context.CancellationToken);
            parsed    = ParseResponse(rawResponse);
            modelUsed = OpusModel;
        }

        // Persist turn to history
        await SaveTurnAsync(body.SessionId, "user",      body.Message,    context.CancellationToken);
        await SaveTurnAsync(body.SessionId, "assistant", parsed.Answer,   context.CancellationToken);

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new
        {
            answer    = parsed.Answer,
            sources,
            modelUsed,
            confidence = parsed.Confidence,
        });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    // ── Anthropic API ────────────────────────────────────────────────────────

    private async Task<(string RawText, string Model)> CallAnthropicAsync(
        string model,
        string fieldsJson,
        IReadOnlyList<object> messages,
        CancellationToken ct)
    {
        var requestBody = new
        {
            model,
            max_tokens = 2048,
            system = new object[]
            {
                new { type = "text", text = SystemPromptText,
                      cache_control = new { type = "ephemeral" } },
                new { type = "text", text = $"Extracted contract fields:\n{fieldsJson}",
                      cache_control = new { type = "ephemeral" } },
            },
            messages,
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpReq.Headers.Add("x-api-key",          _anthropicKey);
        httpReq.Headers.Add("anthropic-version",  "2023-06-01");
        httpReq.Headers.Add("anthropic-beta",      "prompt-caching-2024-07-31");
        httpReq.Content = content;

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(httpReq, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Anthropic error {Status}: {Body}", resp.StatusCode, err);
            throw new HttpRequestException($"Anthropic returned {(int)resp.StatusCode}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return (text, model);
    }

    private static IReadOnlyList<object> BuildMessages(
        IReadOnlyList<ChatTurnEntity> history,
        string userMessage,
        DocumentContext? document)
    {
        var list = new List<object>();

        foreach (var turn in history)
            list.Add(new { role = turn.Role, content = turn.Content });

        if (document is { IsPdf: true })
        {
            list.Add(new
            {
                role    = "user",
                content = new object[]
                {
                    new { type = "document", source = new
                        { type = "base64", media_type = "application/pdf", data = document.Base64 } },
                    new { type = "text", text = userMessage },
                },
            });
        }
        else if (document is { Text: { Length: > 0 } text })
        {
            list.Add(new { role = "user", content = $"Full contract text:\n{text}\n\nQuestion: {userMessage}" });
        }
        else
        {
            list.Add(new { role = "user", content = userMessage });
        }

        return list;
    }

    private static ChatResponseParsed ParseResponse(string raw)
    {
        try
        {
            var json = ExtractJson(raw);
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
        catch
        {
            return new ChatResponseParsed(raw, 0.5, false, ["extracted_fields"]);
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    // ── Document fetch ───────────────────────────────────────────────────────

    private async Task<DocumentContext?> FetchDocumentAsync(
        ContractExtractionEntity entity, CancellationToken ct)
    {
        try
        {
            var (bytes, contentType) = await _blobs.DownloadAsync("contracts", entity.BlobPath, ct);
            var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                || entity.BlobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            return isPdf
                ? new DocumentContext(IsPdf: true, Base64: Convert.ToBase64String(bytes), Text: null)
                : new DocumentContext(IsPdf: false, Base64: null, Text: Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch document for {BlobPath}", entity.BlobPath);
            return null;
        }
    }

    private record DocumentContext(bool IsPdf, string? Base64, string? Text);

    // ── Chat history ─────────────────────────────────────────────────────────

    private async Task<List<ChatTurnEntity>> LoadHistoryAsync(
        string sessionId, CancellationToken ct)
    {
        var table = _tableClient.GetTableClient(ChatHistoryTable);
        var results = new List<ChatTurnEntity>();
        try
        {
            await foreach (var e in table.QueryAsync<ChatTurnEntity>(
                filter: $"PartitionKey eq '{sessionId}'", cancellationToken: ct))
                results.Add(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        results.Sort((a, b) => string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal));

        // Keep last N turns to stay within context limits
        return results.Count > MaxHistoryTurns
            ? results[^MaxHistoryTurns..]
            : results;
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> Plain(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }

    private record ChatRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("correlationId")] string CorrelationId,
        [property: System.Text.Json.Serialization.JsonPropertyName("sessionId")]     string SessionId,
        [property: System.Text.Json.Serialization.JsonPropertyName("message")]       string Message);

    private record ChatResponseParsed(
        string       Answer,
        double       Confidence,
        bool         NeedsDocument,
        List<string> Sources);
}

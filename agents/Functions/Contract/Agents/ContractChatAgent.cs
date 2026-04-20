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
    private const string ChatHistoryTable = "ContractChatHistory";
    private const int    MaxHistoryTurns  = 20;

    private const string SystemPromptBase = """
        You are a contract analyst assistant with access to the user's contract database via tools.

        Use list_contracts to find contracts or answer cross-contract questions (e.g. "which contracts expire next month").
        Use get_contract to retrieve extracted fields for a specific contract.
        Use get_contract_document only when extracted fields lack the detail needed to answer the question.

        Answer accurately and concisely. Never hallucinate contract data, dates, parties, or clauses.
        If you cannot find the answer in the available data, say so clearly.
        """;

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        ChatTool.CreateFunctionTool(
            "list_contracts",
            "List all contracts accessible to the current user with metadata (type, filename, status, dates).",
            BinaryData.FromString("""{"type":"object","properties":{},"required":[]}""")),
        ChatTool.CreateFunctionTool(
            "get_contract",
            "Get the extracted fields for a specific contract by its correlation ID.",
            BinaryData.FromString("""{"type":"object","properties":{"correlationId":{"type":"string","description":"The contract correlation ID"}},"required":["correlationId"]}""")),
        ChatTool.CreateFunctionTool(
            "get_contract_document",
            "Get the full original document text for a contract. Use when extracted fields lack enough detail.",
            BinaryData.FromString("""{"type":"object","properties":{"correlationId":{"type":"string","description":"The contract correlation ID"}},"required":["correlationId"]}""")),
    ];

    private readonly ChatClient          _chatClient;
    private readonly IHttpClientFactory  _httpFactory;
    private readonly string              _apiKey;
    private readonly TableServiceClient  _tableClient;
    private readonly TableStorageService _tableStorage;
    private readonly BlobStorageService  _blobs;
    private readonly ILogger<ContractChatAgent> _logger;

    public ContractChatAgent(
        IHttpClientFactory         httpFactory,
        TableServiceClient         tableClient,
        TableStorageService        tableStorage,
        BlobStorageService         blobs,
        IConfiguration             config,
        ILogger<ContractChatAgent> logger)
    {
        _httpFactory  = httpFactory;
        _tableClient  = tableClient;
        _tableStorage = tableStorage;
        _blobs        = blobs;
        _logger       = logger;
        _apiKey       = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        _chatClient = new OpenAIClient(_apiKey).GetChatClient(MiniModel);
    }

    public async Task<ChatResult> ChatAsync(
        string? contextCorrelationId,
        string sessionId,
        string message,
        string userId,
        bool isAdmin,
        CancellationToken ct)
    {
        var history = await LoadHistoryAsync(sessionId, ct);

        var systemPrompt = contextCorrelationId is not null
            ? $"{SystemPromptBase}\nThe user is currently viewing contract ID: {contextCorrelationId}"
            : SystemPromptBase;

        var messages = new List<ChatMessage> { ChatMessage.CreateSystemMessage(systemPrompt) };
        foreach (var turn in history)
        {
            messages.Add(turn.Role == "user"
                ? ChatMessage.CreateUserMessage(turn.Content)
                : ChatMessage.CreateAssistantMessage(turn.Content));
        }
        messages.Add(ChatMessage.CreateUserMessage(message));

        var options = new ChatCompletionOptions();
        foreach (var tool in Tools) options.Tools.Add(tool);

        string answer;
        while (true)
        {
            var result     = await _chatClient.CompleteChatAsync(messages, options, ct);
            var completion = result.Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion));
                foreach (var call in completion.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(call, userId, isAdmin, ct);
                    _logger.LogInformation("Tool {Tool} called, result length: {Len}", call.FunctionName, toolResult.Length);
                    messages.Add(new ToolChatMessage(call.Id, toolResult));
                }
            }
            else
            {
                answer = completion.Content[0].Text;
                break;
            }
        }

        await SaveTurnAsync(sessionId, "user",      message, ct);
        await SaveTurnAsync(sessionId, "assistant", answer,  ct);

        return new ChatResult(answer, MiniModel);
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct) =>
        call.FunctionName switch
        {
            "list_contracts"        => await ListContractsToolAsync(userId, isAdmin, ct),
            "get_contract"          => await GetContractToolAsync(call, userId, isAdmin, ct),
            "get_contract_document" => await GetContractDocumentToolAsync(call, userId, isAdmin, ct),
            _                       => "Unknown tool",
        };

    private async Task<string> ListContractsToolAsync(
        string userId, bool isAdmin, CancellationToken ct)
    {
        var contracts = await _tableStorage.ListExtractionsAsync(isAdmin ? null : userId, ct);
        var summary = contracts.Select(e => new
        {
            correlationId = e.PartitionKey,
            documentType  = e.DocumentType,
            fileName      = e.FileName,
            status        = e.Status,
            uploadedAt    = e.UploadedAt,
        });
        return JsonSerializer.Serialize(summary);
    }

    private async Task<string> GetContractToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var correlationId = ParseArg(call.FunctionArguments, "correlationId");
        if (correlationId is null) return "Missing correlationId argument";

        var entity = await _tableStorage.GetExtractionAsync(correlationId, ct);
        if (entity is null) return "Contract not found";
        if (!isAdmin && entity.UserId != userId) return "Access denied";

        return string.IsNullOrEmpty(entity.Fields) ? "{}" : entity.Fields;
    }

    private async Task<string> GetContractDocumentToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var correlationId = ParseArg(call.FunctionArguments, "correlationId");
        if (correlationId is null) return "Missing correlationId argument";

        var entity = await _tableStorage.GetExtractionAsync(correlationId, ct);
        if (entity is null) return "Contract not found";
        if (!isAdmin && entity.UserId != userId) return "Access denied";

        var text = await FetchDocumentTextAsync(entity, ct);
        return text ?? "Could not retrieve document";
    }

    private static string? ParseArg(BinaryData args, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            return doc.RootElement.TryGetProperty(key, out var val) ? val.GetString() : null;
        }
        catch { return null; }
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
}

public record ChatResult(string Answer, string ModelUsed);

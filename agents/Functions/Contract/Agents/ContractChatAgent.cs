using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Agents.Contract.Services;
using HqAgent.Shared.Models;
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

        Use find_expiring_contracts for expiry questions.
        Use find_renewal_windows for notice period, renewal, and action deadline questions.
        Use find_contracts_by_person when the question names an employee, consultant, contact, or signatory.
        Use find_contracts_by_counterparty when the question names a customer, supplier, vendor, or other party.
        Use list_contracts to inspect the visible contract portfolio.
        Use get_contract to retrieve extracted fields for a specific contract.
        Use get_contract_document only when extracted fields lack the detail needed to answer the question.

        Answer accurately and concisely. Never hallucinate contract data, dates, parties, or clauses.
        If you cannot find the answer in the available data, say so clearly.
        """;

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        ChatTool.CreateFunctionTool(
            "list_contracts",
            "List all contracts accessible to the current user with normalized metadata, dates, parties, people, and risk flags.",
            BinaryData.FromString("""{"type":"object","properties":{},"required":[]}""")),
        ChatTool.CreateFunctionTool(
            "find_expiring_contracts",
            "Find contracts expiring in a date range. Defaults to the next 90 days when dates are omitted.",
            BinaryData.FromString("""{"type":"object","properties":{"from":{"type":"string","description":"Optional start date, ISO yyyy-MM-dd"},"to":{"type":"string","description":"Optional end date, ISO yyyy-MM-dd"},"contractType":{"type":"string","description":"Optional contract type filter, e.g. NDA or consulting"}},"required":[]}""")),
        ChatTool.CreateFunctionTool(
            "find_renewal_windows",
            "Find contracts with notice deadlines, renewal windows, or expiry action dates in a date range.",
            BinaryData.FromString("""{"type":"object","properties":{"from":{"type":"string","description":"Optional start date, ISO yyyy-MM-dd"},"to":{"type":"string","description":"Optional end date, ISO yyyy-MM-dd"}},"required":[]}""")),
        ChatTool.CreateFunctionTool(
            "find_contracts_by_person",
            "Find contracts that mention or affect an employee, consultant, contact, owner, or signatory.",
            BinaryData.FromString("""{"type":"object","properties":{"personName":{"type":"string","description":"The person name to search for"}},"required":["personName"]}""")),
        ChatTool.CreateFunctionTool(
            "find_contracts_by_counterparty",
            "Find contracts by customer, supplier, vendor, client, or other counterparty name.",
            BinaryData.FromString("""{"type":"object","properties":{"counterparty":{"type":"string","description":"The counterparty name to search for"}},"required":["counterparty"]}""")),
        ChatTool.CreateFunctionTool(
            "get_contract",
            "Get normalized facts and extracted fields for a specific contract by its correlation ID.",
            BinaryData.FromString("""{"type":"object","properties":{"correlationId":{"type":"string","description":"The contract correlation ID"}},"required":["correlationId"]}""")),
        ChatTool.CreateFunctionTool(
            "get_contract_document",
            "Get the full original document text for a contract. Use when extracted fields lack enough detail.",
            BinaryData.FromString("""{"type":"object","properties":{"correlationId":{"type":"string","description":"The contract correlation ID"}},"required":["correlationId"]}""")),
    ];

    private readonly ChatClient          _chatClient;
    private readonly TableServiceClient  _tableClient;
    private readonly IContractIntelligence _contractIntelligence;
    private readonly ILogger<ContractChatAgent> _logger;

    public ContractChatAgent(
        TableServiceClient         tableClient,
        IContractIntelligence      contractIntelligence,
        IConfiguration             config,
        ILogger<ContractChatAgent> logger)
    {
        _tableClient  = tableClient;
        _contractIntelligence = contractIntelligence;
        _logger       = logger;
        var apiKey    = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");

        _chatClient = new OpenAIClient(apiKey).GetChatClient(MiniModel);
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
            "find_expiring_contracts" => await FindExpiringContractsToolAsync(call, userId, isAdmin, ct),
            "find_renewal_windows"  => await FindRenewalWindowsToolAsync(call, userId, isAdmin, ct),
            "find_contracts_by_person" => await FindContractsByPersonToolAsync(call, userId, isAdmin, ct),
            "find_contracts_by_counterparty" => await FindContractsByCounterpartyToolAsync(call, userId, isAdmin, ct),
            "get_contract"          => await GetContractToolAsync(call, userId, isAdmin, ct),
            "get_contract_document" => await GetContractDocumentToolAsync(call, userId, isAdmin, ct),
            _                       => "Unknown tool",
        };

    private async Task<string> ListContractsToolAsync(
        string userId, bool isAdmin, CancellationToken ct)
    {
        var contracts = await _contractIntelligence.ListContractsAsync(Caller(userId, isAdmin), ct);
        return JsonSerializer.Serialize(contracts);
    }

    private async Task<string> FindExpiringContractsToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var from = ParseDateArg(call.FunctionArguments, "from");
        var to = ParseDateArg(call.FunctionArguments, "to");
        var contractType = ParseArg(call.FunctionArguments, "contractType");
        var contracts = await _contractIntelligence.FindExpiringAsync(
            Caller(userId, isAdmin), from, to, contractType, ct);
        return JsonSerializer.Serialize(contracts);
    }

    private async Task<string> FindRenewalWindowsToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var from = ParseDateArg(call.FunctionArguments, "from");
        var to = ParseDateArg(call.FunctionArguments, "to");
        var contracts = await _contractIntelligence.FindRenewalWindowsAsync(
            Caller(userId, isAdmin), from, to, ct);
        return JsonSerializer.Serialize(contracts);
    }

    private async Task<string> FindContractsByPersonToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var personName = ParseArg(call.FunctionArguments, "personName");
        if (personName is null) return "Missing personName argument";
        var contracts = await _contractIntelligence.FindByPersonAsync(
            Caller(userId, isAdmin), personName, ct);
        return JsonSerializer.Serialize(contracts);
    }

    private async Task<string> FindContractsByCounterpartyToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var counterparty = ParseArg(call.FunctionArguments, "counterparty");
        if (counterparty is null) return "Missing counterparty argument";
        var contracts = await _contractIntelligence.FindByCounterpartyAsync(
            Caller(userId, isAdmin), counterparty, ct);
        return JsonSerializer.Serialize(contracts);
    }

    private async Task<string> GetContractToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var correlationId = ParseArg(call.FunctionArguments, "correlationId");
        if (correlationId is null) return "Missing correlationId argument";

        var detail = await _contractIntelligence.GetContractAsync(correlationId, Caller(userId, isAdmin), ct);
        if (detail is null) return "Contract not found";

        return JsonSerializer.Serialize(new
        {
            detail.Summary,
            extracted = string.IsNullOrEmpty(detail.ExtractedFieldsJson)
                ? (JsonElement?)null
                : JsonSerializer.Deserialize<JsonElement>(detail.ExtractedFieldsJson),
        });
    }

    private async Task<string> GetContractDocumentToolAsync(
        ChatToolCall call, string userId, bool isAdmin, CancellationToken ct)
    {
        var correlationId = ParseArg(call.FunctionArguments, "correlationId");
        if (correlationId is null) return "Missing correlationId argument";

        var text = await _contractIntelligence.GetContractDocumentTextAsync(
            correlationId, Caller(userId, isAdmin), ct);
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

    private static DateOnly? ParseDateArg(BinaryData args, string key)
    {
        var value = ParseArg(args, key);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static ContractCallerContext Caller(string userId, bool isAdmin) => new(userId, isAdmin);

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

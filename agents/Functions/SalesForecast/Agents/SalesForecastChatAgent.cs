using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Agents.SalesForecast.Services;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace HqAgent.Agents.SalesForecast.Agents;

public class SalesForecastChatAgent
{
    private const string MiniModel = "gpt-4.1-mini";
    private const int MaxHistoryTurns = 20;

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        ChatTool.CreateFunctionTool(
            "get_monthly_forecast",
            "Get the full revenue forecast for a given year and month, including booked and unbooked consultants.",
            BinaryData.FromString("""{"type":"object","properties":{"year":{"type":"integer"},"month":{"type":"integer"}},"required":["year","month"]}""")),
        ChatTool.CreateFunctionTool(
            "get_consultant_forecast",
            "Get the revenue forecast for a specific consultant by name for a given year and month.",
            BinaryData.FromString("""{"type":"object","properties":{"consultantName":{"type":"string"},"year":{"type":"integer"},"month":{"type":"integer"}},"required":["consultantName","year","month"]}""")),
    ];

    private readonly ChatClient _chatClient;
    private readonly TableServiceClient _tableClient;
    private readonly ISalesForecastIntelligence _forecast;
    private readonly ILogger<SalesForecastChatAgent> _logger;

    public SalesForecastChatAgent(
        TableServiceClient tableClient,
        ISalesForecastIntelligence forecast,
        IConfiguration config,
        ILogger<SalesForecastChatAgent> logger)
    {
        _tableClient = tableClient;
        _forecast = forecast;
        _logger = logger;

        var apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
        _chatClient = new OpenAIClient(apiKey).GetChatClient(MiniModel);
    }

    public async Task<string> ChatAsync(string sessionId, string message, CancellationToken ct)
    {
        var history = await LoadHistoryAsync(sessionId, ct);
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(BuildSystemPrompt()),
            ChatMessage.CreateSystemMessage(BuildLanguageInstruction(message)),
        };

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
            var result = await _chatClient.CompleteChatAsync(messages, options, ct);
            var completion = result.Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion));
                foreach (var call in completion.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(call, ct);
                    _logger.LogInformation("Sales forecast tool {Tool} called, result length: {Len}", call.FunctionName, toolResult.Length);
                    messages.Add(new ToolChatMessage(call.Id, toolResult));
                }
            }
            else
            {
                answer = completion.Content[0].Text;
                break;
            }
        }

        await SaveTurnAsync(sessionId, "user", message, ct);
        await SaveTurnAsync(sessionId, "assistant", answer, ct);
        return answer;
    }

    private async Task<string> ExecuteToolAsync(ChatToolCall call, CancellationToken ct)
    {
        try
        {
            return call.FunctionName switch
            {
                "get_monthly_forecast" => await GetMonthlyForecastToolAsync(call, ct),
                "get_consultant_forecast" => await GetConsultantForecastToolAsync(call, ct),
                _ => "Unknown tool",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sales forecast tool {Tool} failed", call.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    private async Task<string> GetMonthlyForecastToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var year = ParseIntArg(call.FunctionArguments, "year");
        var month = ParseIntArg(call.FunctionArguments, "month");
        if (year is null) return "Missing year argument";
        if (month is null) return "Missing month argument";

        var result = await _forecast.GetMonthlyForecastAsync(year.Value, month.Value, ct);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetConsultantForecastToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var consultantName = ParseArg(call.FunctionArguments, "consultantName");
        var year = ParseIntArg(call.FunctionArguments, "year");
        var month = ParseIntArg(call.FunctionArguments, "month");

        if (consultantName is null) return "Missing consultantName argument";
        if (year is null) return "Missing year argument";
        if (month is null) return "Missing month argument";

        var result = await _forecast.GetConsultantForecastAsync(consultantName, year.Value, month.Value, ct);
        return JsonSerializer.Serialize(result);
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

    private static int? ParseIntArg(BinaryData args, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (!doc.RootElement.TryGetProperty(key, out var val)) return null;
            return val.ValueKind == JsonValueKind.Number ? val.GetInt32() : null;
        }
        catch { return null; }
    }

    private async Task<List<ChatTurnEntity>> LoadHistoryAsync(string sessionId, CancellationToken ct)
    {
        var table = _tableClient.GetTableClient(TableNames.SalesForecastChatHistory);
        var results = new List<ChatTurnEntity>();
        try
        {
            await foreach (var entity in table.QueryAsync<ChatTurnEntity>(
                filter: $"PartitionKey eq '{sessionId}'", cancellationToken: ct))
                results.Add(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        results.Sort((a, b) => string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal));
        return results.Count > MaxHistoryTurns ? results[^MaxHistoryTurns..] : results;
    }

    private async Task SaveTurnAsync(string sessionId, string role, string content, CancellationToken ct)
    {
        var table = _tableClient.GetTableClient(TableNames.SalesForecastChatHistory);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new ChatTurnEntity
        {
            PartitionKey = sessionId,
            RowKey = DateTime.UtcNow.Ticks.ToString("D20"),
            Role = role,
            Content = content,
        }, TableUpdateMode.Replace, ct);
    }

    private static string BuildSystemPrompt()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return $$"""
            You are a sales forecast assistant for a consulting firm. You help management understand monthly revenue estimates, booked vs. unbooked consultants, and pipeline gaps.

            Today's date is {{today:yyyy-MM-dd}}.

            IMPORTANT RULES:
            - For any question about a forecast month, consultant forecast, booked revenue, unbooked revenue, or who is booked, always call a forecast tool before answering.
            - Never answer forecast questions from memory or assumptions.
            - Resolve relative dates from today's date. For example, "this month" means {{today:yyyy-MM}}, and "next month" means the month immediately after that.
            - Reply entirely in the language of the user's latest message unless they explicitly ask you to switch.
            - Do not let older chat history change the response language.
            - Always answer monetary amounts in Swedish kronor (SEK).
            - If the tools return an error or missing data, say that clearly instead of inventing an answer.
            - When a tool returns consultant details such as CalculationDetails, ContractStartDate, ContractEndDate, WorkingDaysIncluded, HoursBeforeUtilization, or UtilizationApplied, use those fields to explain how the forecast was calculated.
            - If a consultant has fewer hours because a contract starts mid-month or ends before month-end, say that explicitly and include the relevant dates.
            - If the user asks what is booked versus estimated, separate booked revenue from unbooked estimated revenue and explain which consultants fall into each group.
            - If asked about something unrelated to sales forecasting or consultant revenue, politely decline and redirect.
            """;
    }

    private static string BuildLanguageInstruction(string message)
    {
        var language = DetectLanguage(message);
        return language == "sv"
            ? "The user's latest message is in Swedish. Reply entirely in Swedish."
            : "The user's latest message is in English. Reply entirely in English.";
    }

    private static string DetectLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "en";

        var sample = $" {message.Trim().ToLowerInvariant()} ";
        if (sample.IndexOfAny(['å', 'ä', 'ö']) >= 0)
            return "sv";

        string[] swedishMarkers =
        [
            " och ", " hur ", " varför ", " vad ", " nästa ", " månad ", " maj ",
            " ge mig ", " timmar ", " bokad ", " uppskattat ", " konsult "
        ];

        return swedishMarkers.Any(sample.Contains) ? "sv" : "en";
    }
}

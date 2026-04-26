using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Agents.HR.Services;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace HqAgent.Agents.HR.Agents;

public class HRChatAgent
{
    private const string MiniModel        = "gpt-4.1-mini";
    private const string ChatHistoryTable = "HRChatHistory";
    private const int    MaxHistoryTurns  = 20;

    private const string SystemPrompt = """
        You are an HR assistant for this company. You manage employees and answer HR-related questions.

        IMPORTANT RULES — follow these without exception:
        - Never calculate salary, Flexible Salary, or utilization from memory or assumptions.
          Always call calculate_salary. The formula and thresholds come from Table Storage and may have changed.
        - Never state the Standard Hours Deduction or utilization target from memory.
          Always call get_hr_config to read the current values.
        - Always present monetary amounts in SEK (kr).
        - Always show the full salary breakdown when answering salary questions, including:
          base salary, Billing Base Rate, hours billed, Standard Hours Deduction, eligible hours, Flexible Salary, and total.

        Available tools:
        - list_employees: list all active employees
        - find_employee: find an employee by name or email
        - add_employee: add a new employee (requires full name, email, start date, base salary in SEK, Billing Base Rate in SEK/hr)
        - update_employee: update one or more fields on an existing employee
        - offboard_employee: mark an employee as offboarded with their last day
        - calculate_salary: calculate monthly salary for an employee given hours billed — always use this
        - get_hr_config: read current Standard Hours Deduction and utilization target from storage — always use this

        When asked about salary for a specific person: call find_employee first to get the employeeId, then calculate_salary.
        When asked about salary for yourself or "my salary": call find_employee with the name of the person asking.
        When asked what the Standard Hours Deduction or utilization target is: call get_hr_config.
        When find_employee returns multiple matches: list the matches and ask the user to clarify which person they mean. Never pick one arbitrarily.

        Only answer HR-related questions. If the question is outside the HR domain, say:
        "I can only help with HR and employee-related questions."
        """;

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        ChatTool.CreateFunctionTool(
            "list_employees",
            "List all active employees with their salary, billing rate, vacation balance, and start date.",
            BinaryData.FromString("""{"type":"object","properties":{},"required":[]}""")),

        ChatTool.CreateFunctionTool(
            "find_employee",
            "Find an employee by name or email. Returns their employeeId and full details.",
            BinaryData.FromString("""{"type":"object","properties":{"nameOrEmail":{"type":"string","description":"Full or partial name, or email address"}},"required":["nameOrEmail"]}""")),

        ChatTool.CreateFunctionTool(
            "add_employee",
            "Add a new employee record.",
            BinaryData.FromString("""{"type":"object","properties":{"fullName":{"type":"string"},"email":{"type":"string"},"startDate":{"type":"string","description":"ISO date, e.g. 2026-05-01"},"baseSalary":{"type":"number","description":"Monthly base salary in SEK"},"billingBaseRate":{"type":"number","description":"Billing Base Rate in SEK per hour — the rate applied to hours billed above the Standard Hours Deduction"},"vacationBalance":{"type":"integer","description":"Vacation days, default 30"}},"required":["fullName","email","startDate","baseSalary","billingBaseRate"]}""")),

        ChatTool.CreateFunctionTool(
            "update_employee",
            "Update one or more fields on an existing employee. Provide employeeId and only the fields to change.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"},"fullName":{"type":"string"},"email":{"type":"string"},"startDate":{"type":"string"},"baseSalary":{"type":"number"},"billingBaseRate":{"type":"number"},"vacationBalance":{"type":"integer"}},"required":["employeeId"]}""")),

        ChatTool.CreateFunctionTool(
            "offboard_employee",
            "Mark an employee as offboarded. Records their last day.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"},"offboardDate":{"type":"string","description":"ISO date of last working day, e.g. 2026-04-30"}},"required":["employeeId","offboardDate"]}""")),

        ChatTool.CreateFunctionTool(
            "calculate_salary",
            "Calculate monthly salary for an employee given hours billed. Reads formula and thresholds live from storage. Always use this — never calculate manually.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"},"hoursBilled":{"type":"number","description":"Total hours billed this month"}},"required":["employeeId","hoursBilled"]}""")),

        ChatTool.CreateFunctionTool(
            "get_hr_config",
            "Get current HR config from storage: Standard Hours Deduction (hours) and utilization target (%). Always call this before stating any threshold or target.",
            BinaryData.FromString("""{"type":"object","properties":{},"required":[]}""")),
    ];

    private readonly ChatClient       _chatClient;
    private readonly TableServiceClient _tableClient;
    private readonly IHRIntelligence  _hr;
    private readonly ILogger<HRChatAgent> _logger;

    public HRChatAgent(
        TableServiceClient   tableClient,
        IHRIntelligence      hr,
        IConfiguration       config,
        ILogger<HRChatAgent> logger)
    {
        _tableClient = tableClient;
        _hr          = hr;
        _logger      = logger;

        var apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
        _chatClient = new OpenAIClient(apiKey).GetChatClient(MiniModel);
    }

    public async Task<string> ChatAsync(
        string sessionId,
        string message,
        CancellationToken ct)
    {
        var history  = await LoadHistoryAsync(sessionId, ct);
        var messages = new List<ChatMessage> { ChatMessage.CreateSystemMessage(SystemPrompt) };

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
                    var toolResult = await ExecuteToolAsync(call, ct);
                    _logger.LogInformation("HR tool {Tool} called, result length: {Len}", call.FunctionName, toolResult.Length);
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

        return answer;
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(ChatToolCall call, CancellationToken ct)
    {
        try
        {
            return call.FunctionName switch
            {
                "list_employees"   => await ListEmployeesToolAsync(ct),
                "find_employee"    => await FindEmployeeToolAsync(call, ct),
                "add_employee"     => await AddEmployeeToolAsync(call, ct),
                "update_employee"  => await UpdateEmployeeToolAsync(call, ct),
                "offboard_employee" => await OffboardEmployeeToolAsync(call, ct),
                "calculate_salary" => await CalculateSalaryToolAsync(call, ct),
                "get_hr_config"    => await GetHRConfigToolAsync(ct),
                _                  => "Unknown tool",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HR tool {Tool} failed", call.FunctionName);
            return $"Tool error: {ex.Message}";
        }
    }

    private async Task<string> ListEmployeesToolAsync(CancellationToken ct)
    {
        var employees = await _hr.ListEmployeesAsync(ct);
        return JsonSerializer.Serialize(employees);
    }

    private async Task<string> FindEmployeeToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var nameOrEmail = ParseArg(call.FunctionArguments, "nameOrEmail");
        if (nameOrEmail is null) return "Missing nameOrEmail argument";

        var matches = await _hr.FindEmployeesAsync(nameOrEmail, ct);
        if (matches.Count == 0) return $"No employee found matching '{nameOrEmail}'";
        return JsonSerializer.Serialize(matches);
    }

    private async Task<string> AddEmployeeToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var args = call.FunctionArguments;
        var fullName        = ParseArg(args, "fullName");
        var email           = ParseArg(args, "email");
        var startDateStr    = ParseArg(args, "startDate");
        var baseSalaryStr   = ParseDecimalArg(args, "baseSalary");
        var billingRateStr  = ParseDecimalArg(args, "billingBaseRate");

        if (fullName is null || email is null || startDateStr is null || baseSalaryStr is null || billingRateStr is null)
            return "Missing required fields: fullName, email, startDate, baseSalary, billingBaseRate";

        if (!DateTimeOffset.TryParse(startDateStr, out var startDate))
            return $"Invalid startDate format: {startDateStr}. Use ISO format, e.g. 2026-05-01";

        var vacationBalance = ParseIntArg(args, "vacationBalance") ?? 30;

        var request = new AddEmployeeRequest(
            FullName: fullName,
            Email: email,
            StartDate: startDate,
            BaseSalary: baseSalaryStr.Value,
            BillingBaseRate: billingRateStr.Value,
            VacationBalance: vacationBalance);
        var employee = await _hr.AddEmployeeAsync(request, ct);
        return JsonSerializer.Serialize(employee);
    }

    private async Task<string> UpdateEmployeeToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseArg(call.FunctionArguments, "employeeId");
        if (employeeId is null) return "Missing employeeId argument";

        var startDateStr = ParseArg(call.FunctionArguments, "startDate");
        DateTimeOffset? startDate = null;
        if (startDateStr is not null && DateTimeOffset.TryParse(startDateStr, out var parsed))
            startDate = parsed;

        var request = new UpdateEmployeeRequest(
            FullName:        ParseArg(call.FunctionArguments, "fullName"),
            Email:           ParseArg(call.FunctionArguments, "email"),
            StartDate:       startDate,
            BaseSalary:      ParseDecimalArg(call.FunctionArguments, "baseSalary"),
            BillingBaseRate: ParseDecimalArg(call.FunctionArguments, "billingBaseRate"),
            VacationBalance: ParseIntArg(call.FunctionArguments, "vacationBalance"));

        var employee = await _hr.UpdateEmployeeAsync(employeeId, request, ct);
        return employee is null
            ? $"Employee {employeeId} not found"
            : JsonSerializer.Serialize(employee);
    }

    private async Task<string> OffboardEmployeeToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId     = ParseArg(call.FunctionArguments, "employeeId");
        var offboardDateStr = ParseArg(call.FunctionArguments, "offboardDate");

        if (employeeId is null) return "Missing employeeId argument";
        if (offboardDateStr is null) return "Missing offboardDate argument";
        if (!DateTimeOffset.TryParse(offboardDateStr, out var offboardDate))
            return $"Invalid offboardDate format: {offboardDateStr}";

        var employee = await _hr.OffboardEmployeeAsync(employeeId, offboardDate, ct);
        return employee is null
            ? $"Employee {employeeId} not found"
            : JsonSerializer.Serialize(employee);
    }

    private async Task<string> CalculateSalaryToolAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId  = ParseArg(call.FunctionArguments, "employeeId");
        var hoursBilled = ParseDecimalArg(call.FunctionArguments, "hoursBilled");

        if (employeeId is null) return "Missing employeeId argument";
        if (hoursBilled is null) return "Missing hoursBilled argument";

        var result = await _hr.CalculateSalaryAsync(employeeId, hoursBilled.Value, ct);
        return result is null
            ? $"Employee {employeeId} not found"
            : JsonSerializer.Serialize(result);
    }

    private async Task<string> GetHRConfigToolAsync(CancellationToken ct)
    {
        var config = await _hr.GetHRConfigAsync(ct);
        return JsonSerializer.Serialize(config);
    }

    // ── Argument parsing ──────────────────────────────────────────────────────

    private static string? ParseArg(BinaryData args, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            return doc.RootElement.TryGetProperty(key, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }

    private static decimal? ParseDecimalArg(BinaryData args, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(args);
            if (!doc.RootElement.TryGetProperty(key, out var val)) return null;
            return val.ValueKind == JsonValueKind.Number ? val.GetDecimal() : null;
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

    // ── Chat history ──────────────────────────────────────────────────────────

    private async Task<List<ChatTurnEntity>> LoadHistoryAsync(string sessionId, CancellationToken ct)
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

    private async Task SaveTurnAsync(string sessionId, string role, string content, CancellationToken ct)
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

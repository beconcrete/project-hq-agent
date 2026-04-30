using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Agents.Contract.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace HqAgent.Agents.HQ.Agents;

public class HqChatAgent
{
    private const string MiniModel     = "gpt-4.1-mini";
    private const int    MaxHistory    = 20;

    private const string SystemPrompt = """
        You are HQ — the unified company assistant. You have access to tools for every domain:
        contracts, employees, customers, projects, and time reporting.

        CONTRACTS: Use contract tools to answer questions about agreements, expiry dates, notice periods,
        renewal windows, payment terms, counterparties, people mentioned in contracts, and consulting assignments.
        Deleted or rejected contracts are not active and must not be treated as available agreements.

        EMPLOYEES: Use employee tools to list employees, get details by email, or find who works on a project.

        CUSTOMERS: Use customer tools to list customers or look up a specific customer by name or ID.

        PROJECTS: Use project tools to list projects, get project details, find projects by customer or employee.
        Resolve project names to IDs using list_projects before calling any project-specific tool.

        TIME REPORTING — conversational flow:
        1. When the user reports time (e.g. "report 2 hours on project X"), call log_time.
           If the project is named (not an ID), call list_projects first to resolve it.
        2. After saving, respond: "Done — X hours logged on [Project] for [date]. What did you work on today?"
           Include the rowKey in your response so you can update the note in the next turn.
        3. When the user replies with what they did, call update_timereport_note with the rowKey from step 2.
        4. Confirm: "Got it — I've added your note to today's entry."
        5. For queries like "how many hours this week?", call query_hours and sum the results.

        MULTI-DOMAIN: You can answer questions that span domains in a single response.
        Example: "which contracts expire next quarter and who are the employees affected?" —
        call find_expiring_contracts then find_employees_by_project for relevant project IDs.

        LANGUAGE: Respond in the same language the user writes in (Swedish or English).
        ACCURACY: Never hallucinate data. If you cannot find something, say so clearly.
        """;

    private static readonly IReadOnlyList<ChatTool> Tools =
    [
        // Contracts
        ChatTool.CreateFunctionTool("list_contracts",
            "List all contracts with normalized metadata, dates, parties, payment, and risk flags.",
            BinaryData.FromString("""{"type":"object","properties":{},"required":[]}""")),
        ChatTool.CreateFunctionTool("find_expiring_contracts",
            "Find contracts expiring in a date range. Defaults to the next 90 days.",
            BinaryData.FromString("""{"type":"object","properties":{"from":{"type":"string","description":"ISO yyyy-MM-dd"},"to":{"type":"string","description":"ISO yyyy-MM-dd"},"contractType":{"type":"string"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("find_renewal_windows",
            "Find contracts with upcoming notice deadlines or renewal windows.",
            BinaryData.FromString("""{"type":"object","properties":{"from":{"type":"string"},"to":{"type":"string"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("find_contracts_by_person",
            "Find contracts that mention or affect a named employee, consultant, or signatory.",
            BinaryData.FromString("""{"type":"object","properties":{"personName":{"type":"string"}},"required":["personName"]}""")),
        ChatTool.CreateFunctionTool("find_contracts_by_counterparty",
            "Find contracts by customer, supplier, or other counterparty name.",
            BinaryData.FromString("""{"type":"object","properties":{"counterparty":{"type":"string"}},"required":["counterparty"]}""")),
        ChatTool.CreateFunctionTool("get_contract",
            "Get normalized facts and extracted fields for a specific contract by its ID.",
            BinaryData.FromString("""{"type":"object","properties":{"contractId":{"type":"string"}},"required":["contractId"]}""")),

        // Employees
        ChatTool.CreateFunctionTool("list_employees",
            "List all active employees with their name, email, seniority, salary, and billing rate.",
            BinaryData.FromString("""{"type":"object","properties":{"includeOffboarded":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_employee",
            "Get details for a specific employee by email address.",
            BinaryData.FromString("""{"type":"object","properties":{"email":{"type":"string"}},"required":["email"]}""")),
        ChatTool.CreateFunctionTool("find_employees_by_project",
            "Find employees assigned to a specific project by project ID.",
            BinaryData.FromString("""{"type":"object","properties":{"projectId":{"type":"string"}},"required":["projectId"]}""")),

        // Customers
        ChatTool.CreateFunctionTool("list_customers",
            "List all customers.",
            BinaryData.FromString("""{"type":"object","properties":{"includeInactive":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_customer",
            "Get details for a customer by ID or name.",
            BinaryData.FromString("""{"type":"object","properties":{"customerIdOrName":{"type":"string"}},"required":["customerIdOrName"]}""")),

        // Projects
        ChatTool.CreateFunctionTool("list_projects",
            "List all projects with customer, status, and assigned employees.",
            BinaryData.FromString("""{"type":"object","properties":{"includeClosedProjects":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_project",
            "Get details for a specific project by ID.",
            BinaryData.FromString("""{"type":"object","properties":{"projectId":{"type":"string"}},"required":["projectId"]}""")),
        ChatTool.CreateFunctionTool("list_projects_by_customer",
            "List all projects for a given customer ID.",
            BinaryData.FromString("""{"type":"object","properties":{"customerId":{"type":"string"}},"required":["customerId"]}""")),
        ChatTool.CreateFunctionTool("list_projects_by_employee",
            "List all projects an employee is assigned to, by email.",
            BinaryData.FromString("""{"type":"object","properties":{"email":{"type":"string"}},"required":["email"]}""")),

        // Timereports
        ChatTool.CreateFunctionTool("log_time",
            "Log a time entry for an employee on a project. Returns a rowKey to use with update_timereport_note.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeEmail":{"type":"string","description":"Email of the reporting employee"},"projectId":{"type":"string","description":"Resolved project GUID"},"date":{"type":"string","description":"ISO yyyy-MM-dd, defaults to today"},"hours":{"type":"number"},"note":{"type":"string","description":"Optional note; omit to ask the user"}},"required":["employeeEmail","projectId","hours"]}""")),
        ChatTool.CreateFunctionTool("update_timereport_note",
            "Update the note on a previously logged time entry using the rowKey returned by log_time.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeEmail":{"type":"string"},"rowKey":{"type":"string"},"note":{"type":"string"}},"required":["employeeEmail","rowKey","note"]}""")),
        ChatTool.CreateFunctionTool("query_hours",
            "Query and sum hours for an employee, project, or customer over a date range.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeEmail":{"type":"string"},"projectId":{"type":"string"},"customerId":{"type":"string"},"from":{"type":"string","description":"ISO yyyy-MM-dd"},"to":{"type":"string","description":"ISO yyyy-MM-dd"}},"required":[]}""")),
    ];

    private readonly ChatClient _chatClient;
    private readonly TableServiceClient _tableClient;
    private readonly IContractIntelligence _contracts;
    private readonly HRTableStorageService _hrStorage;
    private readonly CustomerStorageService _customerStorage;
    private readonly ProjectStorageService _projectStorage;
    private readonly TimereportStorageService _timereportStorage;
    private readonly ILogger<HqChatAgent> _logger;

    public HqChatAgent(
        TableServiceClient       tableClient,
        IContractIntelligence    contracts,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TimereportStorageService timereportStorage,
        IConfiguration           config,
        ILogger<HqChatAgent>     logger)
    {
        _tableClient       = tableClient;
        _contracts         = contracts;
        _hrStorage         = hrStorage;
        _customerStorage   = customerStorage;
        _projectStorage    = projectStorage;
        _timereportStorage = timereportStorage;
        _logger            = logger;

        var apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
        _chatClient = new OpenAIClient(apiKey).GetChatClient(MiniModel);
    }

    public async Task<HqChatResult> ChatAsync(
        string sessionId,
        string message,
        string userId,
        bool   isAdmin,
        CancellationToken ct)
    {
        var caller  = new ContractCallerContext(userId, isAdmin);
        var history = await LoadHistoryAsync(userId, sessionId, ct);

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
                    var toolResult = await ExecuteToolAsync(call, caller, userId, ct);
                    _logger.LogInformation("HQ tool {Tool} called, result length: {Len}", call.FunctionName, toolResult.Length);
                    messages.Add(new ToolChatMessage(call.Id, toolResult));
                }
            }
            else
            {
                answer = completion.Content[0].Text;
                break;
            }
        }

        await SaveTurnAsync(userId, sessionId, "user",      message, ct);
        await SaveTurnAsync(userId, sessionId, "assistant", answer,  ct);

        return new HqChatResult(answer, MiniModel);
    }

    // ── Tool dispatcher ───────────────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(
        ChatToolCall call,
        ContractCallerContext caller,
        string userId,
        CancellationToken ct) =>
        call.FunctionName switch
        {
            // Contracts
            "list_contracts"            => await ListContractsAsync(caller, ct),
            "find_expiring_contracts"   => await FindExpiringAsync(call, caller, ct),
            "find_renewal_windows"      => await FindRenewalWindowsAsync(call, caller, ct),
            "find_contracts_by_person"  => await FindByPersonAsync(call, caller, ct),
            "find_contracts_by_counterparty" => await FindByCounterpartyAsync(call, caller, ct),
            "get_contract"              => await GetContractAsync(call, caller, ct),

            // Employees
            "list_employees"            => await ListEmployeesAsync(call, ct),
            "get_employee"              => await GetEmployeeAsync(call, ct),
            "find_employees_by_project" => await FindEmployeesByProjectAsync(call, ct),

            // Customers
            "list_customers"            => await ListCustomersAsync(call, ct),
            "get_customer"              => await GetCustomerAsync(call, ct),

            // Projects
            "list_projects"             => await ListProjectsAsync(call, ct),
            "get_project"               => await GetProjectAsync(call, ct),
            "list_projects_by_customer" => await ListProjectsByCustomerAsync(call, ct),
            "list_projects_by_employee" => await ListProjectsByEmployeeAsync(call, ct),

            // Timereports
            "log_time"               => await LogTimeAsync(call, userId, ct),
            "update_timereport_note" => await UpdateTimereportNoteAsync(call, ct),
            "query_hours"            => await QueryHoursAsync(call, ct),

            _ => "Unknown tool",
        };

    // ── Contract tools ────────────────────────────────────────────────────────

    private async Task<string> ListContractsAsync(ContractCallerContext caller, CancellationToken ct)
    {
        var items = await _contracts.ListContractsAsync(caller, ct);
        return Serialize(items);
    }

    private async Task<string> FindExpiringAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var from = ParseDate(call, "from");
        var to   = ParseDate(call, "to");
        var type = ParseStr(call, "contractType");
        var items = await _contracts.FindExpiringAsync(caller, from, to, type, ct);
        return Serialize(items);
    }

    private async Task<string> FindRenewalWindowsAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var from  = ParseDate(call, "from");
        var to    = ParseDate(call, "to");
        var items = await _contracts.FindRenewalWindowsAsync(caller, from, to, ct);
        return Serialize(items);
    }

    private async Task<string> FindByPersonAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var name = ParseStr(call, "personName");
        if (name is null) return "Missing personName";
        var items = await _contracts.FindByPersonAsync(caller, name, ct);
        return Serialize(items);
    }

    private async Task<string> FindByCounterpartyAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var cp    = ParseStr(call, "counterparty");
        if (cp is null) return "Missing counterparty";
        var items = await _contracts.FindByCounterpartyAsync(caller, cp, ct);
        return Serialize(items);
    }

    private async Task<string> GetContractAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var id = ParseStr(call, "contractId");
        if (id is null) return "Missing contractId";
        var detail = await _contracts.GetContractAsync(id, caller, ct);
        if (detail is null) return "Contract not found";
        return Serialize(new { detail.Summary,
            extracted = string.IsNullOrEmpty(detail.ExtractedFieldsJson)
                ? (JsonElement?)null
                : JsonSerializer.Deserialize<JsonElement>(detail.ExtractedFieldsJson) });
    }

    // ── Employee tools ────────────────────────────────────────────────────────

    private async Task<string> ListEmployeesAsync(ChatToolCall call, CancellationToken ct)
    {
        var includeOffboarded = ParseBool(call, "includeOffboarded") ?? false;
        var employees = await _hrStorage.ListEmployeesAsync(includeOffboarded, ct);
        return Serialize(employees.Select(e => new
        {
            email          = e.RowKey,
            name           = e.FullName,
            status         = e.Status,
            seniorityLevel = e.SeniorityLevel,
            startDate      = e.StartDate,
            baseSalary     = e.BaseSalary,
            billingBaseRate = e.BillingBaseRate,
        }));
    }

    private async Task<string> GetEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var email = ParseStr(call, "email");
        if (email is null) return "Missing email";
        var e = await _hrStorage.GetEmployeeAsync(email, ct);
        if (e is null) return "Employee not found";
        return Serialize(new
        {
            email          = e.RowKey,
            name           = e.FullName,
            status         = e.Status,
            seniorityLevel = e.SeniorityLevel,
            startDate      = e.StartDate,
            baseSalary     = e.BaseSalary,
            billingBaseRate = e.BillingBaseRate,
            vacationBalance = e.VacationBalance,
        });
    }

    private async Task<string> FindEmployeesByProjectAsync(ChatToolCall call, CancellationToken ct)
    {
        var projectId = ParseStr(call, "projectId");
        if (projectId is null) return "Missing projectId";
        var project = await _projectStorage.GetProjectAsync(projectId, ct);
        if (project is null) return "Project not found";

        string[] emails;
        try { emails = JsonSerializer.Deserialize<string[]>(project.EmployeeEmails) ?? []; }
        catch { emails = []; }

        var employees = new List<object>();
        foreach (var email in emails)
        {
            var e = await _hrStorage.GetEmployeeAsync(email, ct);
            if (e is not null)
                employees.Add(new { email = e.RowKey, name = e.FullName, status = e.Status });
        }
        return Serialize(employees);
    }

    // ── Customer tools ────────────────────────────────────────────────────────

    private async Task<string> ListCustomersAsync(ChatToolCall call, CancellationToken ct)
    {
        var includeInactive = ParseBool(call, "includeInactive") ?? false;
        var customers = await _customerStorage.ListCustomersAsync(includeInactive, ct);
        return Serialize(customers);
    }

    private async Task<string> GetCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var idOrName = ParseStr(call, "customerIdOrName");
        if (idOrName is null) return "Missing customerIdOrName";

        var customer = await _customerStorage.GetCustomerAsync(idOrName, ct)
            ?? await _customerStorage.FindByNameAsync(idOrName, ct);
        if (customer is null) return "Customer not found";
        return Serialize(customer);
    }

    // ── Project tools ─────────────────────────────────────────────────────────

    private async Task<string> ListProjectsAsync(ChatToolCall call, CancellationToken ct)
    {
        var includeClosed = ParseBool(call, "includeClosedProjects") ?? false;
        var projects = await _projectStorage.ListProjectsAsync(includeClosed, ct);
        return Serialize(projects.Select(p => new
        {
            projectId    = p.RowKey,
            name         = p.Name,
            customerId   = p.CustomerId,
            customerName = p.CustomerName,
            status       = p.Status,
            startDate    = p.StartDate,
            endDate      = p.EndDate,
            employeeEmails = TryDeserializeStringArray(p.EmployeeEmails),
        }));
    }

    private async Task<string> GetProjectAsync(ChatToolCall call, CancellationToken ct)
    {
        var id = ParseStr(call, "projectId");
        if (id is null) return "Missing projectId";
        var p = await _projectStorage.GetProjectAsync(id, ct);
        if (p is null) return "Project not found";
        return Serialize(new
        {
            projectId    = p.RowKey,
            name         = p.Name,
            customerId   = p.CustomerId,
            customerName = p.CustomerName,
            status       = p.Status,
            startDate    = p.StartDate,
            endDate      = p.EndDate,
            description  = p.Description,
            employeeEmails = TryDeserializeStringArray(p.EmployeeEmails),
        });
    }

    private async Task<string> ListProjectsByCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var customerId = ParseStr(call, "customerId");
        if (customerId is null) return "Missing customerId";
        var projects = await _projectStorage.ListByCustomerAsync(customerId, ct);
        return Serialize(projects.Select(p => new { projectId = p.RowKey, p.Name, p.Status }));
    }

    private async Task<string> ListProjectsByEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var email = ParseStr(call, "email");
        if (email is null) return "Missing email";
        var projects = await _projectStorage.ListByEmployeeAsync(email, ct);
        return Serialize(projects.Select(p => new { projectId = p.RowKey, p.Name, p.Status }));
    }

    // ── Timereport tools ──────────────────────────────────────────────────────

    private async Task<string> LogTimeAsync(ChatToolCall call, string userId, CancellationToken ct)
    {
        var email     = ParseStr(call, "employeeEmail") ?? userId;
        var projectId = ParseStr(call, "projectId");
        if (projectId is null) return "Missing projectId";

        var hoursRaw = ParseDouble(call, "hours");
        if (hoursRaw is null) return "Missing hours";

        var note   = ParseStr(call, "note") ?? "";
        var dateStr = ParseStr(call, "date");
        var date   = dateStr is not null && DateOnly.TryParse(dateStr, out var d)
            ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        var project = await _projectStorage.GetProjectAsync(projectId, ct);
        var entry   = await _timereportStorage.LogTimeAsync(
            email,
            projectId,
            project?.Name ?? projectId,
            project?.CustomerId ?? "",
            project?.CustomerName ?? "",
            hoursRaw.Value,
            note,
            date,
            ct);

        return Serialize(new
        {
            saved      = true,
            rowKey     = entry.RowKey,
            email,
            projectId,
            projectName = entry.ProjectName,
            hours      = entry.Hours,
            date       = entry.ReportDate,
            note       = entry.Note,
        });
    }

    private async Task<string> UpdateTimereportNoteAsync(ChatToolCall call, CancellationToken ct)
    {
        var email  = ParseStr(call, "employeeEmail");
        var rowKey = ParseStr(call, "rowKey");
        var note   = ParseStr(call, "note");
        if (email is null || rowKey is null || note is null)
            return "Missing employeeEmail, rowKey, or note";

        var updated = await _timereportStorage.UpdateNoteAsync(email, rowKey, note, ct);
        return updated ? """{"updated":true}""" : """{"updated":false,"error":"Entry not found"}""";
    }

    private async Task<string> QueryHoursAsync(ChatToolCall call, CancellationToken ct)
    {
        var email      = ParseStr(call, "employeeEmail");
        var projectId  = ParseStr(call, "projectId");
        var customerId = ParseStr(call, "customerId");
        var from       = ParseDate(call, "from");
        var to         = ParseDate(call, "to");

        var entries = await _timereportStorage.QueryAsync(
            email, projectId, customerId,
            from.HasValue ? new DateOnly(from.Value.Year, from.Value.Month, from.Value.Day) : null,
            to.HasValue   ? new DateOnly(to.Value.Year,   to.Value.Month,   to.Value.Day)   : null,
            ct);

        var total = entries.Sum(e => e.Hours);
        var byDate = entries
            .GroupBy(e => e.ReportDate)
            .Select(g => new { date = g.Key, hours = g.Sum(e => e.Hours), entries = g.Count() })
            .OrderBy(g => g.date);

        return Serialize(new { totalHours = total, breakdown = byDate, entryCount = entries.Count });
    }

    // ── Chat history ──────────────────────────────────────────────────────────

    private async Task<List<HqChatTurnEntity>> LoadHistoryAsync(
        string userId, string sessionId, CancellationToken ct)
    {
        var table   = _tableClient.GetTableClient(TableNames.HQChatHistory);
        var results = new List<HqChatTurnEntity>();
        var prefix  = $"{sessionId}_";
        try
        {
            await foreach (var e in table.QueryAsync<HqChatTurnEntity>(
                filter: $"PartitionKey eq '{userId}' and RowKey ge '{prefix}'",
                cancellationToken: ct))
                results.Add(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        results.Sort((a, b) => string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal));
        return results.Count > MaxHistory ? results[^MaxHistory..] : results;
    }

    private async Task SaveTurnAsync(
        string userId, string sessionId, string role, string content, CancellationToken ct)
    {
        var table = _tableClient.GetTableClient(TableNames.HQChatHistory);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new HqChatTurnEntity
        {
            PartitionKey = userId,
            RowKey       = $"{sessionId}_{DateTime.UtcNow.Ticks:D20}",
            SessionId    = sessionId,
            Role         = role,
            Content      = content,
        }, TableUpdateMode.Replace, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ParseStr(ChatToolCall call, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(call.FunctionArguments);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    private static DateOnly? ParseDate(ChatToolCall call, string key)
    {
        var s = ParseStr(call, key);
        return DateOnly.TryParse(s, out var d) ? d : null;
    }

    private static bool? ParseBool(ChatToolCall call, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(call.FunctionArguments);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return null;
            return v.ValueKind == JsonValueKind.True;
        }
        catch { return null; }
    }

    private static double? ParseDouble(ChatToolCall call, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(call.FunctionArguments);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return null;
            return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        }
        catch { return null; }
    }

    private static string[] TryDeserializeStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
}

public record HqChatResult(string Answer, string ModelUsed);

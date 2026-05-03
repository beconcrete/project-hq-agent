using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using BeConcrete.EmbeddingCore.Services;
using HqAgent.Agents.Contract.Services;
using HqAgent.Agents.HQ.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace HqAgent.Agents.HQ.Agents;

public class HqChatAgent
{
    private const string MiniModel  = "gpt-4.1-mini";
    private const int    MaxHistory = 20;

    private const string SystemPrompt = """
        You are HQ — the unified company assistant. You have access to tools for every domain:
        contracts, employees, customers, projects, and time reporting.

        SEARCH FIRST — THE GOLDEN RULE:
        Users always refer to things by name, never by ID. Whenever the user mentions a name
        (customer, project, employee, contract), call search_entities first to find matching entities
        and retrieve their IDs. Then use those IDs in follow-up domain tools.

        Example flows:
        - "projects for Cibus" → search_entities("Cibus") → entityId = customerId → list_projects_by_customer(customerId)
        - "hours on AI Transformation" → search_entities("AI Transformation") → entityId = projectId → query_hours(projectId)
        - "Björn's time this month" → search_entities("Björn Eriksen") → entityId = employeeId (GUID) → query_hours(employeeId)
        - "contracts for Volvo" → search_entities("Volvo") → contract and customer hits → get_contract(id) for each
        - "who works on AI Transformation?" → search_entities("AI Transformation") → projectId → get_project(id) → employeeIds

        Only skip search_entities when:
        - The user asks for a full list with no filter ("list all projects", "show all employees")
        - You already have an ID from a previous tool result in this conversation
        - It is a time reporting write operation (log_time, update_timereport_note, delete_timereport)

        NEVER pass a human-readable name to a tool that expects an ID — it will always return empty results.

        SEARCH RESULTS ARE HINTS, NOT FACTS:
        search_entities returns similarity matches from an index that may be stale. A search hit does
        NOT confirm an entity exists. Before telling the user something exists, or blocking a create
        because of a name clash, ALWAYS verify with the appropriate get_* tool (get_project, get_customer,
        get_employee, get_contract). If get_* returns "not found", the search result is stale — ignore it
        and proceed (e.g. go ahead and create the project the user asked for).

        CONTRACTS: Use contract tools to answer questions about agreements, expiry dates, notice periods,
        renewal windows, payment terms, counterparties, people, and consulting assignments.
        Deleted or rejected contracts are not active and must not be treated as available agreements.

        PROJECTS: To add or remove employees from an existing project, ALWAYS use update_project with
        addEmployeeIds or removeEmployeeIds (pass employeeId GUIDs, not emails). NEVER call create_project
        to modify an existing project.

        CUSTOMERS: When creating a customer only the name is required; ask for optional fields if you have them.

        EMPLOYEES: When creating an employee, workEmail and fullName are required; other fields are optional.
        Employees are identified by their employeeId (GUID). Use search_entities to resolve a name to an employeeId.
        Use link_auth_identity (admin only) to connect an employee's Auth0 login identity to their record.

        TIME REPORTING — conversational flow:
        1. When the user reports time (e.g. "report 2 hours on project X"), call search_entities first
           to resolve the project name to a projectId, then call log_time.
        2. After saving, respond: "Done — X hours logged on [Project] for [date]. What did you work on today?"
           Include the rowKey in your response so you can update the note in the next turn.
        3. When the user replies with what they did, call update_timereport_note with the rowKey from step 2.
        4. Confirm: "Got it — I've added your note to today's entry."
        5. For queries like "how many hours this week?" or "who reported on project X?", resolve any names
           via search_entities first, then call query_hours with the resolved IDs.
        6. To remove entries: call query_hours first to find the matching entries and their rowKeys,
           then call delete_timereport for each one. Never claim an entry was deleted without calling the tool.

        LANGUAGE: Respond in the same language the user writes in (Swedish or English).
        ACCURACY: Never hallucinate data. If you cannot find something, say so clearly.
        TONE: Be concise and direct. Never end responses with offers to help further or
        pleasantries like "feel free to ask", "let me know if you need anything else",
        "if you have more questions", or similar filler. Just answer and stop.
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
        ChatTool.CreateFunctionTool("get_contract",
            "Get normalized facts and extracted fields for a specific contract by its ID.",
            BinaryData.FromString("""{"type":"object","properties":{"contractId":{"type":"string"}},"required":["contractId"]}""")),

        // Employees
        ChatTool.CreateFunctionTool("list_employees",
            "List all active employees with their name, workEmail, employeeId, seniority, salary, and billing rate.",
            BinaryData.FromString("""{"type":"object","properties":{"includeOffboarded":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_employee",
            "Get details for a specific employee by their employeeId (GUID).",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"}},"required":["employeeId"]}""")),
        ChatTool.CreateFunctionTool("create_employee",
            "Create a new employee record. workEmail and fullName are required. Returns the new employeeId.",
            BinaryData.FromString("""{"type":"object","properties":{"workEmail":{"type":"string","description":"Work email, e.g. bjorn@beconcrete.se"},"fullName":{"type":"string"},"startDate":{"type":"string","description":"ISO yyyy-MM-dd"},"baseSalary":{"type":"number"},"billingBaseRate":{"type":"number"},"seniorityLevel":{"type":"string"}},"required":["workEmail","fullName"]}""")),

        // Customers
        ChatTool.CreateFunctionTool("list_customers",
            "List all customers.",
            BinaryData.FromString("""{"type":"object","properties":{"includeInactive":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_customer",
            "Get details for a customer by ID or name.",
            BinaryData.FromString("""{"type":"object","properties":{"customerIdOrName":{"type":"string"}},"required":["customerIdOrName"]}""")),
        ChatTool.CreateFunctionTool("create_customer",
            "Create a new customer. Only name is required.",
            BinaryData.FromString("""{"type":"object","properties":{"name":{"type":"string"},"orgNumber":{"type":"string"},"country":{"type":"string"},"primaryContactName":{"type":"string"},"primaryContactEmail":{"type":"string"},"notes":{"type":"string"}},"required":["name"]}""")),

        // Projects
        ChatTool.CreateFunctionTool("list_projects",
            "List all projects with customer, status, and assigned employee IDs.",
            BinaryData.FromString("""{"type":"object","properties":{"includeClosedProjects":{"type":"boolean"}},"required":[]}""")),
        ChatTool.CreateFunctionTool("get_project",
            "Get details for a specific project by ID.",
            BinaryData.FromString("""{"type":"object","properties":{"projectId":{"type":"string"}},"required":["projectId"]}""")),
        ChatTool.CreateFunctionTool("create_project",
            "Create a new project. Name and customerId are required. NEVER call this to add an employee to an existing project — use update_project instead.",
            BinaryData.FromString("""{"type":"object","properties":{"name":{"type":"string"},"customerId":{"type":"string"},"customerName":{"type":"string"},"startDate":{"type":"string","description":"ISO yyyy-MM-dd"},"endDate":{"type":"string","description":"ISO yyyy-MM-dd"},"description":{"type":"string"},"employeeIds":{"type":"array","items":{"type":"string"},"description":"employeeId GUIDs to assign"}},"required":["name","customerId"]}""")),
        ChatTool.CreateFunctionTool("update_project",
            "Update an existing project. Only the fields you provide are changed. To add employees pass addEmployeeIds; to remove pass removeEmployeeIds. Never use create_project for updates.",
            BinaryData.FromString("""{"type":"object","properties":{"projectId":{"type":"string"},"name":{"type":"string"},"status":{"type":"string","enum":["active","closed"]},"description":{"type":"string"},"startDate":{"type":"string","description":"ISO yyyy-MM-dd"},"endDate":{"type":"string","description":"ISO yyyy-MM-dd"},"addEmployeeIds":{"type":"array","items":{"type":"string"},"description":"employeeId GUIDs to add to the team"},"removeEmployeeIds":{"type":"array","items":{"type":"string"},"description":"employeeId GUIDs to remove from the team"}},"required":["projectId"]}""")),

        // Employees (continued)
        ChatTool.CreateFunctionTool("update_employee",
            "Update fields on an existing employee by employeeId. Only the fields you provide are changed. Use status='offboarded' when an employee leaves. workEmail, loginEmail, and auth0Subject can all be updated independently.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"},"fullName":{"type":"string"},"workEmail":{"type":"string","description":"Work email address"},"status":{"type":"string","enum":["active","offboarded"]},"baseSalary":{"type":"number"},"billingBaseRate":{"type":"number"},"seniorityLevel":{"type":"string"},"offboardDate":{"type":"string","description":"ISO yyyy-MM-dd"}},"required":["employeeId"]}""")),
        ChatTool.CreateFunctionTool("delete_employee",
            "Permanently delete an employee record by employeeId.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"}},"required":["employeeId"]}""")),
        ChatTool.CreateFunctionTool("link_auth_identity",
            "Admin: link an Auth0 login identity to an employee record. Sets auth0Subject (the Auth0 sub claim) and/or loginEmail (the email used for social login). Use this when an employee's login email differs from their work email.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string"},"auth0Subject":{"type":"string","description":"Auth0 sub claim, e.g. google-oauth2|105... or windowslive|abc..."},"loginEmail":{"type":"string","description":"Email used to log in, if different from work email"}},"required":["employeeId"]}""")),

        // Customers (continued)
        ChatTool.CreateFunctionTool("update_customer",
            "Update fields on an existing customer. Only the fields you provide are changed.",
            BinaryData.FromString("""{"type":"object","properties":{"customerId":{"type":"string"},"name":{"type":"string"},"orgNumber":{"type":"string"},"country":{"type":"string"},"primaryContactName":{"type":"string"},"primaryContactEmail":{"type":"string"},"notes":{"type":"string"},"status":{"type":"string","enum":["active","inactive"]}},"required":["customerId"]}""")),
        ChatTool.CreateFunctionTool("delete_customer",
            "Permanently delete a customer record by ID.",
            BinaryData.FromString("""{"type":"object","properties":{"customerId":{"type":"string"}},"required":["customerId"]}""")),

        // Projects (continued)
        ChatTool.CreateFunctionTool("delete_project",
            "Permanently delete a project by ID.",
            BinaryData.FromString("""{"type":"object","properties":{"projectId":{"type":"string"}},"required":["projectId"]}""")),

        // Contracts (continued)
        ChatTool.CreateFunctionTool("link_contract_to_customer",
            "Link a contract to a customer. Use this when the agent identifies which customer a contract belongs to.",
            BinaryData.FromString("""{"type":"object","properties":{"contractId":{"type":"string"},"customerId":{"type":"string"}},"required":["contractId","customerId"]}""")),
        ChatTool.CreateFunctionTool("delete_contract",
            "Permanently delete a contract record by ID. Use when the user wants to remove a duplicate or superseded version.",
            BinaryData.FromString("""{"type":"object","properties":{"contractId":{"type":"string"}},"required":["contractId"]}""")),

        // Cross-entity search
        ChatTool.CreateFunctionTool("search_entities",
            "Primary name resolution and discovery tool. Call this whenever the user mentions any name (person, customer, project, contract). Returns ranked hits with entityType and entityId across all domains. Use the returned IDs for follow-up get_* or query_* calls.",
            BinaryData.FromString("""{"type":"object","properties":{"query":{"type":"string","description":"Natural language search query — use the name or description the user provided"},"limit":{"type":"integer","description":"Max results to return, default 15"}},"required":["query"]}""")),

        // Timereports
        ChatTool.CreateFunctionTool("log_time",
            "Log a time entry for an employee on a project. Pass the employeeId (GUID). Returns a rowKey to use with update_timereport_note.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string","description":"employeeId (GUID) of the reporting employee"},"projectId":{"type":"string","description":"Resolved project GUID"},"date":{"type":"string","description":"ISO yyyy-MM-dd, defaults to today"},"hours":{"type":"number"},"note":{"type":"string","description":"Optional note; omit to ask the user"}},"required":["employeeId","projectId","hours"]}""")),
        ChatTool.CreateFunctionTool("update_timereport_note",
            "Update the note on a previously logged time entry using the rowKey returned by log_time.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string","description":"employeeId (GUID)"},"rowKey":{"type":"string"},"note":{"type":"string"}},"required":["employeeId","rowKey","note"]}""")),
        ChatTool.CreateFunctionTool("delete_timereport",
            "Permanently delete a specific time entry by its rowKey and the employee's employeeId. Call query_hours first to obtain the rowKey.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string","description":"employeeId (GUID) of the employee"},"rowKey":{"type":"string","description":"Row key of the specific entry to delete"}},"required":["employeeId","rowKey"]}""")),
        ChatTool.CreateFunctionTool("query_hours",
            "Query hours for an employee, project, or customer over a date range. Returns each individual entry with its rowKey, date, employeeId, workEmail, hours, and note.",
            BinaryData.FromString("""{"type":"object","properties":{"employeeId":{"type":"string","description":"employeeId (GUID)"},"projectId":{"type":"string"},"customerId":{"type":"string"},"from":{"type":"string","description":"ISO yyyy-MM-dd"},"to":{"type":"string","description":"ISO yyyy-MM-dd"}},"required":[]}""")),
    ];

    private readonly ChatClient _chatClient;
    private readonly TableServiceClient _tableClient;
    private readonly IContractIntelligence _contracts;
    private readonly TableStorageService _contractStorage;
    private readonly HRTableStorageService _hrStorage;
    private readonly CustomerStorageService _customerStorage;
    private readonly ProjectStorageService _projectStorage;
    private readonly TimereportStorageService _timereportStorage;
    private readonly EmbeddingOrchestrator _embeddings;
    private readonly EmbeddingCacheService _cache;
    private readonly EmbeddingService _embeddingService;
    private readonly ILogger<HqChatAgent> _logger;

    public HqChatAgent(
        TableServiceClient       tableClient,
        IContractIntelligence    contracts,
        TableStorageService      contractStorage,
        HRTableStorageService    hrStorage,
        CustomerStorageService   customerStorage,
        ProjectStorageService    projectStorage,
        TimereportStorageService timereportStorage,
        EmbeddingOrchestrator    embeddings,
        EmbeddingCacheService    cache,
        EmbeddingService         embeddingService,
        IConfiguration           config,
        ILogger<HqChatAgent>     logger)
    {
        _tableClient       = tableClient;
        _contracts         = contracts;
        _contractStorage   = contractStorage;
        _hrStorage         = hrStorage;
        _customerStorage   = customerStorage;
        _projectStorage    = projectStorage;
        _timereportStorage = timereportStorage;
        _embeddings        = embeddings;
        _cache             = cache;
        _embeddingService  = embeddingService;
        _logger            = logger;

        var apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
        _chatClient = new OpenAIClient(apiKey).GetChatClient(MiniModel);
    }

    public async Task<HqChatResult> ChatAsync(
        string sessionId,
        string message,
        string userId,
        string userEmail,
        string auth0Subject,
        bool   isAdmin,
        CancellationToken ct)
    {
        var caller  = new ContractCallerContext(userId, isAdmin);
        var history = await LoadHistoryAsync(userId, sessionId, ct);

        // Identify the signed-in user's employee record via Auth0 subject or login email.
        var userEmployee = await _hrStorage.FindByAuthAsync(auth0Subject, userEmail, ct);
        var subPart      = !string.IsNullOrEmpty(auth0Subject) ? $", auth0Subject: {auth0Subject}" : "";
        var userContext  = userEmployee is not null
            ? $"The signed-in user is {userEmployee.FullName} (employeeId: {userEmployee.RowKey}, workEmail: {userEmployee.WorkEmail}{subPart}{(isAdmin ? ", role: admin" : "")})."
            : !string.IsNullOrEmpty(userEmail)
                ? $"The signed-in user has email {userEmail}{subPart}{(isAdmin ? " (admin)" : "")}. No matching employee record found."
                : $"No employee record found for the signed-in user{subPart}{(isAdmin ? " (admin)" : "")}.";

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(SystemPrompt),
            ChatMessage.CreateSystemMessage(userContext),
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
            var result     = await _chatClient.CompleteChatAsync(messages, options, ct);
            var completion = result.Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion));
                foreach (var call in completion.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(call, caller, userEmployee?.RowKey, ct);
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
        string? userEmployeeId,
        CancellationToken ct) =>
        call.FunctionName switch
        {
            // Cross-entity search
            "search_entities"           => await SearchEntitiesAsync(call, ct),

            // Contracts
            "list_contracts"            => await ListContractsAsync(caller, ct),
            "find_expiring_contracts"   => await FindExpiringAsync(call, caller, ct),
            "find_renewal_windows"      => await FindRenewalWindowsAsync(call, caller, ct),
            "get_contract"              => await GetContractAsync(call, caller, ct),

            // Employees
            "list_employees"            => await ListEmployeesAsync(call, ct),
            "get_employee"              => await GetEmployeeAsync(call, ct),
            "create_employee"           => await CreateEmployeeAsync(call, ct),
            "update_employee"           => await UpdateEmployeeAsync(call, ct),
            "delete_employee"           => await DeleteEmployeeAsync(call, ct),
            "link_auth_identity"        => await LinkAuthIdentityAsync(call, ct),

            // Customers
            "list_customers"            => await ListCustomersAsync(call, ct),
            "get_customer"              => await GetCustomerAsync(call, ct),
            "create_customer"           => await CreateCustomerAsync(call, ct),
            "update_customer"           => await UpdateCustomerAsync(call, ct),
            "delete_customer"           => await DeleteCustomerAsync(call, ct),

            // Projects
            "list_projects"             => await ListProjectsAsync(call, ct),
            "get_project"               => await GetProjectAsync(call, ct),
            "create_project"            => await CreateProjectAsync(call, ct),
            "update_project"            => await UpdateProjectAsync(call, ct),
            "delete_project"            => await DeleteProjectAsync(call, ct),

            // Contracts (continued)
            "link_contract_to_customer" => await LinkContractToCustomerAsync(call, ct),
            "delete_contract"           => await DeleteContractAsync(call, ct),

            // Timereports
            "log_time"               => await LogTimeAsync(call, userEmployeeId, ct),
            "update_timereport_note" => await UpdateTimereportNoteAsync(call, ct),
            "delete_timereport"      => await DeleteTimereportAsync(call, ct),
            "query_hours"            => await QueryHoursAsync(call, ct),

            _ => "Unknown tool",
        };

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task<string> SearchEntitiesAsync(ChatToolCall call, CancellationToken ct)
    {
        var query = ParseStr(call, "query");
        if (query is null) return "Missing query";
        var limit = (int?)ParseDouble(call, "limit") ?? 15;

        await _cache.EnsureLoadedAsync(ct);
        var queryVector = await _embeddingService.GenerateAsync(query, ct);
        var hits = _cache.Search(queryVector, limit);

        return Serialize(hits.Select(h => new
        {
            entityType = h.EntityType,
            entityId   = h.EntityId,
            score      = MathF.Round(h.Score, 3),
            snippet    = h.Snippet,
        }));
    }

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

    private async Task<string> GetContractAsync(ChatToolCall call, ContractCallerContext caller, CancellationToken ct)
    {
        var id = ParseStr(call, "contractId");
        if (id is null) return "Missing contractId";
        var detail = await _contracts.GetContractAsync(id, caller, ct);
        if (detail is null) return "Contract not found";
        return Serialize(new
        {
            detail.Summary,
            linkedCustomerIds   = TryDeserializeStringArray(detail.Entity.LinkedCustomerIds),
            linkedCustomerNames = TryDeserializeStringArray(detail.Entity.LinkedCustomerNames),
            extracted = string.IsNullOrEmpty(detail.ExtractedFieldsJson)
                ? (JsonElement?)null
                : JsonSerializer.Deserialize<JsonElement>(detail.ExtractedFieldsJson),
        });
    }

    // ── Employee tools ────────────────────────────────────────────────────────

    private async Task<string> ListEmployeesAsync(ChatToolCall call, CancellationToken ct)
    {
        var includeOffboarded = ParseBool(call, "includeOffboarded") ?? false;
        var employees = await _hrStorage.ListEmployeesAsync(includeOffboarded, ct);
        return Serialize(employees.Select(e => new
        {
            employeeId      = e.RowKey,
            name            = e.FullName,
            workEmail       = e.WorkEmail,
            status          = e.Status,
            seniorityLevel  = e.SeniorityLevel,
            startDate       = e.StartDate,
            baseSalary      = e.BaseSalary,
            billingBaseRate = e.BillingBaseRate,
        }));
    }

    private async Task<string> GetEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        if (employeeId is null) return "Missing employeeId";
        var e = await _hrStorage.GetEmployeeAsync(employeeId, ct);
        if (e is null) return "Employee not found";
        return Serialize(new
        {
            employeeId      = e.RowKey,
            name            = e.FullName,
            workEmail       = e.WorkEmail,
            loginEmail      = e.LoginEmail,
            status          = e.Status,
            seniorityLevel  = e.SeniorityLevel,
            startDate       = e.StartDate,
            baseSalary      = e.BaseSalary,
            billingBaseRate = e.BillingBaseRate,
            vacationBalance = e.VacationBalance,
        });
    }

    private async Task<string> CreateEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var workEmail = ParseStr(call, "workEmail");
        var fullName  = ParseStr(call, "fullName");
        if (string.IsNullOrWhiteSpace(workEmail)) return "Missing workEmail";
        if (string.IsNullOrWhiteSpace(fullName))  return "Missing fullName";

        var entity = new EmployeeEntity
        {
            RowKey          = Guid.NewGuid().ToString(),
            WorkEmail       = workEmail.ToLowerInvariant(),
            FullName        = fullName,
            Status          = "active",
            BaseSalary      = ParseDouble(call, "baseSalary")      ?? 0,
            BillingBaseRate = ParseDouble(call, "billingBaseRate") ?? 0,
            SeniorityLevel  = ParseStr(call, "seniorityLevel")     ?? "",
            StartDate       = DateTimeOffset.UtcNow,
        };

        var startStr = ParseStr(call, "startDate");
        if (startStr is not null && DateOnly.TryParse(startStr, out var start))
            entity.StartDate = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        await _hrStorage.WriteEmployeeAsync(entity, ct);
        await _embeddings.IndexAsync(entity, ct);
        return Serialize(new { created = true, employeeId = entity.RowKey, workEmail = entity.WorkEmail, fullName = entity.FullName });
    }

    private async Task<string> UpdateEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        if (string.IsNullOrWhiteSpace(employeeId)) return "Missing employeeId";

        var existing = await _hrStorage.GetEmployeeAsync(employeeId, ct);
        if (existing is null) return "Employee not found";

        var fullName        = ParseStr(call, "fullName");
        var workEmail       = ParseStr(call, "workEmail");
        var status          = ParseStr(call, "status");
        var seniorityLevel  = ParseStr(call, "seniorityLevel");
        var baseSalary      = ParseDouble(call, "baseSalary");
        var billingBaseRate = ParseDouble(call, "billingBaseRate");
        var offboardStr     = ParseStr(call, "offboardDate");

        if (fullName        is not null) existing.FullName        = fullName;
        if (workEmail       is not null) existing.WorkEmail       = workEmail.ToLowerInvariant();
        if (status          is not null) existing.Status          = status;
        if (seniorityLevel  is not null) existing.SeniorityLevel  = seniorityLevel;
        if (baseSalary      is not null) existing.BaseSalary      = baseSalary.Value;
        if (billingBaseRate is not null) existing.BillingBaseRate = billingBaseRate.Value;
        if (offboardStr     is not null && DateOnly.TryParse(offboardStr, out var offboard))
            existing.OffboardDate = new DateTimeOffset(offboard.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        await _hrStorage.WriteEmployeeAsync(existing, ct);
        await _embeddings.IndexAsync(existing, ct);
        return Serialize(new { updated = true, employeeId = existing.RowKey, workEmail = existing.WorkEmail, fullName = existing.FullName, status = existing.Status });
    }

    private async Task<string> DeleteEmployeeAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        if (string.IsNullOrWhiteSpace(employeeId)) return "Missing employeeId";
        await _hrStorage.DeleteEmployeeAsync(employeeId, ct);
        _cache.RemoveEntry("employee", employeeId);
        return Serialize(new { deleted = true, employeeId });
    }

    private async Task<string> LinkAuthIdentityAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId   = ParseStr(call, "employeeId");
        var auth0Subject = ParseStr(call, "auth0Subject");
        var loginEmail   = ParseStr(call, "loginEmail");
        if (string.IsNullOrWhiteSpace(employeeId)) return "Missing employeeId";

        var existing = await _hrStorage.GetEmployeeAsync(employeeId, ct);
        if (existing is null) return "Employee not found";

        if (auth0Subject is not null) existing.Auth0Subject = auth0Subject;
        if (loginEmail   is not null) existing.LoginEmail   = loginEmail.ToLowerInvariant();

        await _hrStorage.WriteEmployeeAsync(existing, ct);
        await _embeddings.IndexAsync(existing, ct);
        return Serialize(new { linked = true, employeeId, auth0Subject = existing.Auth0Subject, loginEmail = existing.LoginEmail });
    }

    // ── Customer tools ────────────────────────────────────────────────────────

    private async Task<string> ListCustomersAsync(ChatToolCall call, CancellationToken ct)
    {
        var includeInactive = ParseBool(call, "includeInactive") ?? false;
        var customers = await _customerStorage.ListCustomersAsync(includeInactive, ct);
        return Serialize(customers.Select(c => new
        {
            customerId        = c.RowKey,
            c.Name,
            c.Status,
            linkedContractIds = TryDeserializeStringArray(c.LinkedContractIds),
        }));
    }

    private async Task<string> GetCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var idOrName = ParseStr(call, "customerIdOrName");
        if (idOrName is null) return "Missing customerIdOrName";

        var customer = await _customerStorage.GetCustomerAsync(idOrName, ct)
            ?? await _customerStorage.FindByNameAsync(idOrName, ct);
        if (customer is null) return "Customer not found";
        return Serialize(new
        {
            customerId          = customer.RowKey,
            customer.Name,
            customer.OrgNumber,
            customer.Country,
            customer.PrimaryContactName,
            customer.PrimaryContactEmail,
            customer.Status,
            customer.Notes,
            linkedContractIds   = TryDeserializeStringArray(customer.LinkedContractIds),
        });
    }

    private async Task<string> CreateCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var name = ParseStr(call, "name");
        if (string.IsNullOrWhiteSpace(name)) return "Missing name";

        var entity = new CustomerEntity
        {
            Name                = name,
            OrgNumber           = ParseStr(call, "orgNumber")           ?? "",
            Country             = ParseStr(call, "country")             ?? "",
            PrimaryContactName  = ParseStr(call, "primaryContactName")  ?? "",
            PrimaryContactEmail = ParseStr(call, "primaryContactEmail") ?? "",
            Notes               = ParseStr(call, "notes")               ?? "",
            Status              = "active",
        };

        await _customerStorage.WriteCustomerAsync(entity, ct);
        await _embeddings.IndexAsync(entity, ct);
        return Serialize(new { created = true, customerId = entity.RowKey, name = entity.Name });
    }

    private async Task<string> UpdateCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var customerId = ParseStr(call, "customerId");
        if (string.IsNullOrWhiteSpace(customerId)) return "Missing customerId";

        var existing = await _customerStorage.GetCustomerAsync(customerId, ct);
        if (existing is null) return "Customer not found";

        var name                = ParseStr(call, "name");
        var orgNumber           = ParseStr(call, "orgNumber");
        var country             = ParseStr(call, "country");
        var primaryContactName  = ParseStr(call, "primaryContactName");
        var primaryContactEmail = ParseStr(call, "primaryContactEmail");
        var notes               = ParseStr(call, "notes");
        var status              = ParseStr(call, "status");

        if (name                is not null) existing.Name                = name;
        if (orgNumber           is not null) existing.OrgNumber           = orgNumber;
        if (country             is not null) existing.Country             = country;
        if (primaryContactName  is not null) existing.PrimaryContactName  = primaryContactName;
        if (primaryContactEmail is not null) existing.PrimaryContactEmail = primaryContactEmail;
        if (notes               is not null) existing.Notes               = notes;
        if (status              is not null) existing.Status              = status;

        await _customerStorage.WriteCustomerAsync(existing, ct);
        await _embeddings.IndexAsync(existing, ct);
        return Serialize(new { updated = true, customerId = existing.RowKey, name = existing.Name });
    }

    private async Task<string> DeleteCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var customerId = ParseStr(call, "customerId");
        if (string.IsNullOrWhiteSpace(customerId)) return "Missing customerId";
        await _customerStorage.DeleteCustomerAsync(customerId, ct);
        _cache.RemoveEntry("customer", customerId);
        return Serialize(new { deleted = true, customerId });
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
            employeeIds  = TryDeserializeStringArray(p.EmployeeIds),
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
            employeeIds  = TryDeserializeStringArray(p.EmployeeIds),
        });
    }

    private async Task<string> CreateProjectAsync(ChatToolCall call, CancellationToken ct)
    {
        var name       = ParseStr(call, "name");
        var customerId = ParseStr(call, "customerId");
        if (string.IsNullOrWhiteSpace(name))       return "Missing name";
        if (string.IsNullOrWhiteSpace(customerId)) return "Missing customerId";

        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(call.FunctionArguments);
            if (doc.RootElement.TryGetProperty("employeeIds", out var arr))
                ids = arr.EnumerateArray().Select(e => e.GetString() ?? "")
                         .Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        }
        catch { }

        var customer = await _customerStorage.GetCustomerAsync(customerId, ct);
        var entity = new ProjectEntity
        {
            Name         = name,
            CustomerId   = customerId,
            CustomerName = customer?.Name ?? ParseStr(call, "customerName") ?? "",
            Status       = "active",
            Description  = ParseStr(call, "description")  ?? "",
            EmployeeIds  = JsonSerializer.Serialize(ids),
        };

        var startStr = ParseStr(call, "startDate");
        if (startStr is not null && DateOnly.TryParse(startStr, out var start))
            entity.StartDate = start.ToDateTime(TimeOnly.MinValue);

        var endStr = ParseStr(call, "endDate");
        if (endStr is not null && DateOnly.TryParse(endStr, out var end))
            entity.EndDate = end.ToDateTime(TimeOnly.MinValue);

        await _projectStorage.WriteProjectAsync(entity, ct);
        await _embeddings.IndexAsync(entity, ct);
        return Serialize(new { created = true, projectId = entity.RowKey, name = entity.Name, customerId });
    }

    private async Task<string> UpdateProjectAsync(ChatToolCall call, CancellationToken ct)
    {
        var projectId = ParseStr(call, "projectId");
        if (string.IsNullOrWhiteSpace(projectId)) return "Missing projectId";

        var existing = await _projectStorage.GetProjectAsync(projectId, ct);
        if (existing is null) return "Project not found";

        var name        = ParseStr(call, "name");
        var status      = ParseStr(call, "status");
        var description = ParseStr(call, "description");
        var startStr    = ParseStr(call, "startDate");
        var endStr      = ParseStr(call, "endDate");

        if (name        is not null) existing.Name        = name;
        if (status      is not null) existing.Status      = status;
        if (description is not null) existing.Description = description;
        if (startStr    is not null && DateOnly.TryParse(startStr, out var start))
            existing.StartDate = start.ToDateTime(TimeOnly.MinValue);
        if (endStr      is not null && DateOnly.TryParse(endStr, out var end))
            existing.EndDate = end.ToDateTime(TimeOnly.MinValue);

        // Merge employeeId sets
        var currentIds = TryDeserializeStringArray(existing.EmployeeIds)
            .Select(id => id.ToLowerInvariant()).ToHashSet();

        try
        {
            using var doc = JsonDocument.Parse(call.FunctionArguments);
            if (doc.RootElement.TryGetProperty("addEmployeeIds", out var add))
                foreach (var e in add.EnumerateArray())
                {
                    var id = e.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) currentIds.Add(id);
                }
            if (doc.RootElement.TryGetProperty("removeEmployeeIds", out var remove))
                foreach (var e in remove.EnumerateArray())
                {
                    var id = e.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) currentIds.Remove(id);
                }
        }
        catch { }

        existing.EmployeeIds = JsonSerializer.Serialize(currentIds.ToArray());

        await _projectStorage.WriteProjectAsync(existing, ct);
        await _embeddings.IndexAsync(existing, ct);
        return Serialize(new
        {
            updated     = true,
            projectId   = existing.RowKey,
            name        = existing.Name,
            status      = existing.Status,
            employeeIds = currentIds.ToArray(),
        });
    }

    private async Task<string> DeleteProjectAsync(ChatToolCall call, CancellationToken ct)
    {
        var projectId = ParseStr(call, "projectId");
        if (string.IsNullOrWhiteSpace(projectId)) return "Missing projectId";
        await _projectStorage.DeleteProjectAsync(projectId, ct);
        _cache.RemoveEntry("project", projectId);
        return Serialize(new { deleted = true, projectId });
    }

    private async Task<string> LinkContractToCustomerAsync(ChatToolCall call, CancellationToken ct)
    {
        var contractId = ParseStr(call, "contractId");
        var customerId = ParseStr(call, "customerId");
        if (string.IsNullOrWhiteSpace(contractId)) return "Missing contractId";
        if (string.IsNullOrWhiteSpace(customerId)) return "Missing customerId";

        var customer = await _customerStorage.GetCustomerAsync(customerId, ct);
        if (customer is null) return "Customer not found";

        var contract = await _contractStorage.GetExtractionAsync(contractId, ct);
        if (contract is null) return "Contract not found";

        var existingIds   = TryDeserializeStringArray(contract.LinkedCustomerIds).ToList();
        var existingNames = TryDeserializeStringArray(contract.LinkedCustomerNames).ToList();
        if (!existingIds.Contains(customerId, StringComparer.OrdinalIgnoreCase))
        {
            existingIds.Add(customerId);
            existingNames.Add(customer.Name);
        }

        await _contractStorage.UpdateLinkedCustomersAsync(contractId, existingIds, existingNames, ct);
        await _customerStorage.LinkContractAsync(customerId, contractId, ct);

        var updated = await _contractStorage.GetExtractionAsync(contractId, ct);
        if (updated is not null) await _embeddings.IndexAsync(updated, ct);
        await _embeddings.IndexAsync(customer, ct);

        return Serialize(new { linked = true, contractId, customerId, customerName = customer.Name });
    }

    private async Task<string> DeleteContractAsync(ChatToolCall call, CancellationToken ct)
    {
        var contractId = ParseStr(call, "contractId");
        if (string.IsNullOrWhiteSpace(contractId)) return "Missing contractId";
        var deleted = await _contractStorage.HardDeleteContractAsync(contractId, ct);
        if (deleted) _cache.RemoveEntry("contract", contractId);
        return Serialize(new { deleted, contractId });
    }

    // ── Timereport tools ──────────────────────────────────────────────────────

    private async Task<string> LogTimeAsync(ChatToolCall call, string? userEmployeeId, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId") ?? userEmployeeId ?? "";
        if (string.IsNullOrWhiteSpace(employeeId)) return "Missing employeeId";

        var projectId = ParseStr(call, "projectId");
        if (projectId is null) return "Missing projectId";

        var hoursRaw = ParseDouble(call, "hours");
        if (hoursRaw is null) return "Missing hours";

        var note    = ParseStr(call, "note") ?? "";
        var dateStr = ParseStr(call, "date");
        var date    = dateStr is not null && DateOnly.TryParse(dateStr, out var d)
            ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        var employee = await _hrStorage.GetEmployeeAsync(employeeId, ct);
        var project  = await _projectStorage.GetProjectAsync(projectId, ct);
        var entry    = await _timereportStorage.LogTimeAsync(
            employeeId,
            employee?.WorkEmail ?? "",
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
            saved       = true,
            rowKey      = entry.RowKey,
            employeeId,
            workEmail   = entry.WorkEmail,
            projectId,
            projectName = entry.ProjectName,
            hours       = entry.Hours,
            date        = entry.ReportDate,
            note        = entry.Note,
        });
    }

    private async Task<string> UpdateTimereportNoteAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        var rowKey     = ParseStr(call, "rowKey");
        var note       = ParseStr(call, "note");
        if (employeeId is null || rowKey is null || note is null)
            return "Missing employeeId, rowKey, or note";

        var updated = await _timereportStorage.UpdateNoteAsync(employeeId, rowKey, note, ct);
        return updated ? """{"updated":true}""" : """{"updated":false,"error":"Entry not found"}""";
    }

    private async Task<string> DeleteTimereportAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        var rowKey     = ParseStr(call, "rowKey");
        if (employeeId is null || rowKey is null)
            return "Missing employeeId or rowKey";

        var deleted = await _timereportStorage.DeleteAsync(employeeId, rowKey, ct);
        return deleted ? """{"deleted":true}""" : """{"deleted":false,"error":"Entry not found"}""";
    }

    private async Task<string> QueryHoursAsync(ChatToolCall call, CancellationToken ct)
    {
        var employeeId = ParseStr(call, "employeeId");
        var projectId  = ParseStr(call, "projectId");
        var customerId = ParseStr(call, "customerId");
        var from       = ParseDate(call, "from");
        var to         = ParseDate(call, "to");

        var entries = await _timereportStorage.QueryAsync(
            employeeId, projectId, customerId,
            from.HasValue ? new DateOnly(from.Value.Year, from.Value.Month, from.Value.Day) : null,
            to.HasValue   ? new DateOnly(to.Value.Year,   to.Value.Month,   to.Value.Day)   : null,
            ct);

        var total = entries.Sum(e => e.Hours);
        return Serialize(new
        {
            totalHours = total,
            entryCount = entries.Count,
            entries    = entries.Select(e => new
            {
                rowKey     = e.RowKey,
                date       = e.ReportDate,
                employeeId = e.PartitionKey,
                workEmail  = e.WorkEmail,
                hours      = e.Hours,
                note       = e.Note,
            }),
        });
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

# Microsoft Agents Framework (MAF) — Patterns and Learnings

Packages: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`

> **We use OpenAI, not Anthropic, for MAF workflows.** See the [Model provider decision](#model-provider-decision--openai-only) section.

---

## Workflow builder — use `AgentWorkflowBuilder`, not `HandoffWorkflowBuilder`

`HandoffWorkflowBuilder` is deprecated/experimental (`MAAIW001` warning). Always use:

```csharp
#pragma warning disable MAAIW001  // required — build errors without it despite IDE hints
var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(startingAgent)
    .WithHandoff(agentA, agentB)       // A can hand off to B
    .WithHandoff(agentB, agentA)       // B can return to A (if needed)
    .Build();                           // returns Workflow, not AIAgent
#pragma warning restore MAAIW001
```

Declare every handoff direction explicitly — the framework only routes where you tell it to.

### The `#pragma warning disable MAAIW001` is NOT optional

IDE hints may say "remove unnecessary suppression" — ignore them. Removing the pragma causes a hard build error. This has been verified. Keep the pragma.

---

## Executing a workflow — `InProcessExecution`

Do not call `.RunAsync()` on a wrapped agent. Use `InProcessExecution`:

```csharp
await using var run = await InProcessExecution.OpenStreamingAsync(
    workflow, sessionId: someUniqueId);

await run.TrySendMessageAsync(userMessage);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

var sb = new StringBuilder();
await foreach (var evt in run.WatchStreamAsync().WithCancellation(ct))
{
    if (evt is AgentResponseUpdateEvent update)
        sb.Append(update.Update.Text ?? "");
    else if (evt is WorkflowErrorEvent err)
        throw err.Exception ?? new InvalidOperationException("Workflow error");
    else if (evt is WorkflowOutputEvent)
        break;
}

var fullText = sb.ToString();
```

The `sessionId` scopes the conversation. Use a correlationId or similar unique key per job.

### Key event types

| Event | Meaning |
|---|---|
| `AgentResponseUpdateEvent` | Text chunk from any agent — append to a `StringBuilder` |
| `WorkflowOutputEvent` | Workflow complete — break the loop |
| `WorkflowErrorEvent` | Unhandled exception inside the workflow — rethrow |

---

## Parsing workflow output — use the outermost JSON object

The MAF workflow produces text output via `AgentResponseUpdateEvent` chunks. For structured extraction jobs the final agent is instructed to output a single JSON object. However, if that JSON object contains nested objects (e.g. an `extractedFields` property that is itself an object), naively scanning for the "last" JSON object in the output gives you the innermost nested object, not the top-level result — all your expected fields will be missing.

**Always extract the first brace-balanced, parseable JSON object** (the outermost one):

```csharp
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
```

`TryExtractJsonAt` counts braces to find the balanced end of a JSON object. The outer loop tries each `{` in order. The first one that is both brace-balanced AND parses as valid JSON is the outermost result object.

See `agents/Functions/Contract/Agents/ContractOrchestratorAgent.cs` for the full implementation.

---

## Model provider decision — OpenAI only

**We use OpenAI for MAF workflows. Do not use Anthropic with MAF handoff.**

### Why

MAF's handoff mechanism works by passing the full conversation history — including assistant messages from the previous agent — to the next agent. This is called **assistant message prefill**. Anthropic's API rejects this pattern: it returns an error when a conversation starts with an assistant turn.

OpenAI accepts assistant message prefill, so the triage → extraction handoff works correctly — the extraction agent sees the triage agent's classification in the conversation history and builds on it.

### Model assignments

| Step | Model | Reason |
|---|---|---|
| PDF text extraction | `gpt-4.1-mini` | Cheap, fast, handles the file content type |
| Triage / classification | `gpt-4.1-mini` | Cheap, fast |
| Field extraction | `gpt-4.1` | Best accuracy for contract analysis |

### NuGet packages

```xml
<PackageReference Include="OpenAI" Version="2.*" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.*" />
<PackageReference Include="Microsoft.Agents.AI" Version="1.1.*" />
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.1.*" />
```

Do **not** add `Microsoft.Agents.AI.Anthropic` — it is not used and the Anthropic provider does not support the handoff pattern.

### Wiring up OpenAI chat clients

```csharp
var openAiClient = new OpenAIClient(apiKey);
IChatClient triageChatClient     = openAiClient.GetChatClient("gpt-4.1-mini").AsIChatClient();
IChatClient extractionChatClient = openAiClient.GetChatClient("gpt-4.1").AsIChatClient();

var triageAgent = new ChatClientAgent(triageChatClient, new ChatClientAgentOptions
{
    Name        = "triage",
    Description = "Classifies the contract document type",
    ChatOptions = new ChatOptions { Instructions = TriageInstructions },
});
```

---

## PDF handling — extract text before entering the MAF workflow

MAF `ChatMessage` takes text content. PDFs must be extracted to plain text **before** building the `ChatMessage` for the workflow. Use the OpenAI `file` content type with inline base64:

```csharp
var body = JsonSerializer.Serialize(new
{
    model      = "gpt-4.1-mini",
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
                new { type = "text", text = "Extract all text content verbatim, preserving structure. No commentary." }
            }
        }
    }
});
```

Then pass the extracted text string into the MAF workflow as `new ChatMessage(ChatRole.User, [new TextContent(text)])`.

---

## Single-shot workflow pattern (queue-triggered jobs)

For background processing jobs — queue triggers, blob triggers — create agents and the workflow fresh inside the method that handles each message. Each invocation gets its own isolated empty history. No `ChatHistoryProvider` or history persistence is needed.

```csharp
public async Task<ExtractionResult> RunAsync(ContractMessage msg, CancellationToken ct)
{
    // Build agents fresh — each invocation is independent
    var triageAgent    = new ChatClientAgent(_triageChatClient, ...);
    var extractAgent   = new ChatClientAgent(_extractionChatClient, ...);

    var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
        .WithHandoff(triageAgent, extractAgent)
        .Build();

    await using var run = await InProcessExecution.OpenStreamingAsync(
        workflow, sessionId: msg.CorrelationId);
    // ...
}
```

The `IChatClient` instances (`_triageChatClient`, `_extractionChatClient`) can be long-lived singletons — only the workflow and agents need to be scoped per invocation.

## Checklist for a new MAF workflow

- Keep the workflow bounded and job-like. Use MAF when handoff between specialist agents adds value.
- Use OpenAI chat clients; do not introduce Anthropic for handoff workflows.
- Create agents and `AgentWorkflowBuilder` inside the invocation so history is isolated per job.
- Use a stable `sessionId`, normally the job correlation ID.
- Emit a single parseable JSON object for structured jobs.
- Parse the first brace-balanced JSON object, not the last nested object.
- Keep the `MAAIW001` suppression around `AgentWorkflowBuilder` handoff code.
- Add a unit test for any parser/normalizer that consumes workflow output.
- Document the new workflow in this file before adding future domains.

---

## Agent registration — no interface needed for single implementations

If there is only one implementation of a workflow service, register it directly without an interface. Interfaces add indirection without benefit when there is nothing to swap.

```csharp
// Program.cs
services.AddSingleton<ContractOrchestratorAgent>();

// ContractIngestion.cs
public ContractIngestion(ContractOrchestratorAgent orchestrator, ...)
```

---

## Shared conversation history — `ChatHistoryProvider`

Only needed for interactive chat sessions where agents must see each other's messages across turns or requests.

### Contract chat decision

`ContractOrchestratorAgent` uses MAF because ingestion is a bounded triage → extraction workflow where handoff is valuable.

`ContractChatAgent` currently remains a direct OpenAI tool-calling loop. That is intentional for now: interactive chat needs persisted user/assistant turns and pragmatic tool execution, while reusable behavior lives below it in `IContractIntelligence`. Future agents should call `IContractIntelligence` for deterministic contract capabilities, and the chat agent should be viewed as a frontend conversational layer over those same capabilities.

```csharp
var historyProvider = new MyChatHistoryProvider(...);

var agentA = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name                = "...",
    ChatHistoryProvider = historyProvider,   // ← same instance on every agent
});
```

### What to persist vs what NOT to persist

- **Store**: external user messages, assistant text responses
- **Do NOT store**: `FunctionCallContent` or `FunctionResultContent` — orphaned tool pairs cause the model to reject the conversation on reload

```csharp
private static bool IsToolRelated(ChatMessage msg) =>
    msg.Contents.Any(c => c is FunctionCallContent or FunctionResultContent);
```

### Message source attribution

`AgentRequestMessageSourceAttribution.SourceType`:
- `External` — real user input → persist
- `ChatHistory` — already in storage → skip
- `AIContextProvider` — transient injections → skip

---

## Defining tools — `[Description]` + `AIFunctionFactory`

```csharp
public class MyApiTool
{
    [Description("List all items for the active session")]
    public async Task<string> ListItems()
    {
        var items = await _service.ListAsync(...);
        return JsonSerializer.Serialize(items);
    }
}

// In workflow setup:
Tools = [
    AIFunctionFactory.Create(_myApiTool.ListItems),
    AIFunctionFactory.Create(_myApiTool.GetItem),
]
```

Tools return `string`. On error, return a user-readable error string — do not throw, as exceptions propagate out of the workflow.

---

## Context injection — `AIContextProvider`

Use `AIContextProvider` implementations to inject per-request context (user ID, session data, locale) as a system message. Injected messages are transient — they must NOT be stored to history (they are re-added by the provider on every turn).

---

## Compaction — managing context window

For long-running chat sessions, add a `CompactionProvider` to prevent the context window from filling up. Not needed for single-shot processing jobs.

```csharp
var pipeline = new PipelineCompactionStrategy([
    new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(2048), 2),
    new SummarizationCompactionStrategy(chatClient, CompactionTriggers.TokensExceed(8192), 8),
    new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(20)),
    new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(32768), 10),
]);
var compactionProvider = new CompactionProvider(pipeline);
```

---

## Streaming to SSE (for chat endpoints)

```csharp
httpContext.Response.ContentType = "text/event-stream";
httpContext.Response.Headers.CacheControl = "no-cache";
httpContext.Response.Headers["X-Accel-Buffering"] = "no";
httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

// Flush headers early — agent processing can take several seconds
await httpContext.Response.WriteAsync(": connected\n\n");
await httpContext.Response.Body.FlushAsync();

await foreach (var evt in run.WatchStreamAsync())
{
    if (evt is AgentResponseUpdateEvent update && !string.IsNullOrEmpty(update.Update.Text))
    {
        await httpContext.Response.WriteAsync($"data: {{\"text\":\"{EscapeJson(update.Update.Text)}\"}}\n\n");
        await httpContext.Response.Body.FlushAsync();
    }
    else if (evt is WorkflowOutputEvent)
    {
        await httpContext.Response.WriteAsync("data: [DONE]\n\n");
        break;
    }
}
```

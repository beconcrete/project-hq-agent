# Microsoft Agents Framework (MAF) — Patterns and Learnings

Packages: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Agents.AI.Anthropic`

---

## Workflow builder — use `AgentWorkflowBuilder`, not `HandoffWorkflowBuilder`

`HandoffWorkflowBuilder` is deprecated/experimental (`MAAIW001` warning). Always use:

```csharp
#pragma warning disable MAAIW001  // evaluation API — suppress required
var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(startingAgent)
    .WithHandoff(agentA, agentB)       // A can hand off to B
    .WithHandoff(agentB, agentA)       // B can return to A (if needed)
    .Build();                           // returns Workflow, not AIAgent
#pragma warning restore MAAIW001
```

Declare every handoff direction explicitly — the framework only routes where you tell it to.

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

## Shared conversation history — `ChatHistoryProvider`

By default each agent starts with empty history. To let agents see each other's messages within the same workflow session AND across requests, all agents must share the same `ChatHistoryProvider` instance.

```csharp
var historyProvider = new MyChatHistoryProvider(...);

var agentA = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "...",
    Description = "...",
    ChatOptions = new ChatOptions { Instructions = "..." },
    ChatHistoryProvider = historyProvider,   // ← same instance on every agent
});
```

For **single-shot jobs** (one run = one result, no persistence needed) an in-memory provider is sufficient. Build the agents and workflow fresh inside the method that executes the job so each run gets its own empty history.

### What to persist vs what NOT to persist

From `StoreChatHistoryAsync`:
- **Store**: external user messages, assistant text responses
- **Do NOT store**: `FunctionCallContent` or `FunctionResultContent` messages — orphaned tool-call/result pairs cause the model to reject the conversation on subsequent loads

```csharp
private static bool IsToolRelated(ChatMessage msg) =>
    msg.Contents.Any(c => c is FunctionCallContent or FunctionResultContent);
```

### Message source attribution

`AgentRequestMessageSourceAttribution.SourceType` tells you where a message came from:
- `External` — real user input → persist
- `ChatHistory` — already in storage → skip (would duplicate)
- `AIContextProvider` — transient injections (context, compaction summaries) → skip (re-added each turn)

---

## Defining tools — `[Description]` + `AIFunctionFactory`

Tools are regular C# methods on injected services. Decorate with `[Description]` and wrap with `AIFunctionFactory.Create`:

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

For long-running chat sessions, add a `CompactionProvider` to prevent the context window from filling up. Strategies execute in order from gentlest to most aggressive:

```csharp
var pipeline = new PipelineCompactionStrategy([
    new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(2048), 2),
    new SummarizationCompactionStrategy(chatClient, CompactionTriggers.TokensExceed(8192), 8),
    new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(20)),
    new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(32768), 10),
]);
var compactionProvider = new CompactionProvider(pipeline);
```

Not needed for single-shot processing jobs.

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

---

## Anthropic integration

`Microsoft.Agents.AI.Anthropic` provides the `AsAIAgent()` extension on `IAnthropicClient`:

```csharp
var agent = anthropicClient.AsAIAgent(
    model: "claude-sonnet-4-6",
    instructions: "...",
    name: "agent-name",
    description: "What this agent does — used by the router",
    loggerFactory: loggerFactory);
```

The `description` is used by the handoff router to decide which agent to route to. Make it specific.

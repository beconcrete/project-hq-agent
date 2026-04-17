using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using ContractOrchestratorAgent.Models;
using ContractOrchestratorAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Storage ───────────────────────────────────────────────────────────────────
var storageConnStr = builder.Configuration["STORAGE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("STORAGE_CONNECTION_STRING is not configured");

builder.Services.AddSingleton(new BlobServiceClient(storageConnStr));
builder.Services.AddSingleton(new TableServiceClient(storageConnStr));

// ── Claude (raw HttpClient — see AnthropicHttpClient for why) ─────────────────
builder.Services.AddHttpClient<AnthropicHttpClient>();

// ── Agent services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<BlobDownloadService>();
builder.Services.AddScoped<ClaudeService>();
builder.Services.AddScoped<TableStorageService>();
builder.Services.AddScoped<ContractProcessor>();

// ── Dapr ──────────────────────────────────────────────────────────────────────
builder.Services.AddDaprClient();

var app = builder.Build();

// ── Health check ──────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// ── Dapr input binding endpoint ───────────────────────────────────────────────
// Dapr calls this when a message arrives on the contract-processing queue.
// Returning 2xx removes the message from the queue; returning 5xx causes a retry.
app.MapPost("/contract-processing-queue", async (
    HttpRequest                    httpRequest,
    ContractProcessor              processor,
    ILogger<Program>               logger,
    CancellationToken              ct) =>
{
    string body;
    using (var reader = new StreamReader(httpRequest.Body))
        body = await reader.ReadToEndAsync(ct);

    logger.LogInformation("Dapr binding message received: {Body}", body);

    ContractProcessingMessage? message;
    try
    {
        message = JsonSerializer.Deserialize<ContractProcessingMessage>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to deserialise binding message: {Body}", body);
        return Results.BadRequest("Invalid message format");
    }

    if (message is null)
    {
        logger.LogError("Message deserialised to null");
        return Results.BadRequest("Empty message body");
    }

    try
    {
        await processor.ProcessAsync(message, ct);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Processing failed for correlationId:{CorrelationId}", message.CorrelationId);
        return Results.StatusCode(500); // Dapr retries on non-2xx
    }
});

app.Run();

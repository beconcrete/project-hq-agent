using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using ContractOrchestratorAgent.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Storage;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ── Storage ───────────────────────────────────────────────────────────────────
var storageConnStr = builder.Configuration["STORAGE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("STORAGE_CONNECTION_STRING is not configured");

builder.Services.AddSingleton(new BlobServiceClient(storageConnStr));
builder.Services.AddSingleton(new TableServiceClient(storageConnStr));
builder.Services.AddSingleton(new QueueServiceClient(storageConnStr));

// ── AI model client ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IAIModelClient, AnthropicHttpClient>();

// ── Agent services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<BlobStorageService>();
builder.Services.AddScoped<ContractAnalysisService>();
builder.Services.AddScoped<TableStorageService>();
builder.Services.AddScoped<ContractProcessor>();

var app = builder.Build();

// ── Health check ──────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// ── Contract processing endpoint ──────────────────────────────────────────────
// Accepts a ContractMessage as JSON and runs the analysis pipeline.
// Returns 200 on success, 500 on failure (callers may retry).
app.MapPost("/process", async (
    HttpRequest           httpRequest,
    ContractProcessor     processor,
    ILogger<Program>      logger,
    CancellationToken     ct) =>
{
    string body;
    using (var reader = new StreamReader(httpRequest.Body))
        body = await reader.ReadToEndAsync(ct);

    ContractMessage? message;
    try
    {
        message = JsonSerializer.Deserialize<ContractMessage>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to deserialise message: {Body}", body);
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
        return Results.StatusCode(500);
    }
});

app.Run();

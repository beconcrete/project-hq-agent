using ContractOrchestratorAgent.Models;
using Dapr.Client;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Top-level orchestrator: download → classify → extract → persist → notify.
/// </summary>
public class ContractProcessor
{
    private readonly BlobDownloadService  _blobs;
    private readonly ClaudeService        _claude;
    private readonly TableStorageService  _table;
    private readonly DaprClient           _dapr;
    private readonly ILogger<ContractProcessor> _logger;

    private const string PubSubName = "hq-pubsub";
    private const string TopicName  = "contract-completed";

    public ContractProcessor(
        BlobDownloadService       blobs,
        ClaudeService             claude,
        TableStorageService       table,
        DaprClient                dapr,
        ILogger<ContractProcessor> logger)
    {
        _blobs  = blobs;
        _claude = claude;
        _table  = table;
        _dapr   = dapr;
        _logger = logger;
    }

    public async Task ProcessAsync(ContractProcessingMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Processing contract — correlationId:{CorrelationId} blob:{Blob}",
            message.CorrelationId, message.BlobName);

        // 1. Download blob
        var (contractBytes, contentType) = await _blobs.DownloadAsync(
            message.ContainerName, message.BlobName, ct);

        // Normalise to a media type Claude accepts
        var mediaType = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/pdf"; // default — expand when supporting DOCX

        // 2. Classify + extract via Claude
        var (extraction, modelUsed) = await _claude.ExtractAsync(contractBytes, mediaType, ct);

        _logger.LogInformation(
            "Extraction complete — docType:{DocType} confidence:{C:P0} model:{Model}",
            extraction.DocumentType, extraction.Confidence, modelUsed);

        // 3. Persist to Table Storage
        await _table.WriteExtractionAsync(
            message.CorrelationId, message.BlobName, extraction, modelUsed, ct);

        // 4. Publish completion event via Dapr pub/sub (best-effort — WebSocket handler subscribes)
        try
        {
            await _dapr.PublishEventAsync(
                pubsubName : PubSubName,
                topicName  : TopicName,
                data       : new
                {
                    correlationId = message.CorrelationId,
                    documentType  = extraction.DocumentType,
                    status        = "completed",
                    processedAt   = DateTime.UtcNow,
                },
                cancellationToken: ct);

            _logger.LogInformation(
                "Published {Topic} event for {CorrelationId}", TopicName, message.CorrelationId);
        }
        catch (Exception ex)
        {
            // Pub/sub is not yet wired — log and continue rather than failing the whole job
            _logger.LogWarning(ex,
                "Could not publish {Topic} event for {CorrelationId} (Dapr pub/sub not ready?)",
                TopicName, message.CorrelationId);
        }
    }
}

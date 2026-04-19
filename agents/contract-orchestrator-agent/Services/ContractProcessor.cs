using System.Text.Json;
using Azure.Storage.Queues;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;

namespace ContractOrchestratorAgent.Services;

public class ContractProcessor
{
    private readonly BlobStorageService  _blobs;
    private readonly ContractAnalysisService _analysis;
    private readonly TableStorageService _table;
    private readonly QueueServiceClient  _queues;
    private readonly ILogger<ContractProcessor> _logger;

    private const string CompletedQueueName = "contract-completed";

    public ContractProcessor(
        BlobStorageService           blobs,
        ContractAnalysisService      analysis,
        TableStorageService          table,
        QueueServiceClient           queues,
        ILogger<ContractProcessor>   logger)
    {
        _blobs    = blobs;
        _analysis = analysis;
        _table    = table;
        _queues   = queues;
        _logger   = logger;
    }

    public async Task ProcessAsync(ContractMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Processing contract — correlationId:{CorrelationId} blob:{Blob}",
            message.CorrelationId, message.BlobName);

        var (contractBytes, contentType) = await _blobs.DownloadAsync(
            message.ContainerName, message.BlobName, ct);

        var mediaType = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/pdf";

        var extraction = await _analysis.AnalyzeAsync(contractBytes, mediaType, ct);

        _logger.LogInformation(
            "Analysis complete — docType:{DocType} pendingReview:{Pending} model:{Model}",
            extraction.DocumentType, extraction.PendingReview, extraction.ModelUsed);

        await _table.WriteExtractionAsync(message.CorrelationId, message.BlobName, extraction, ct);

        // Notify downstream (WebSocket handler or other consumers) via Azure Queue Storage
        try
        {
            var queue = _queues.GetQueueClient(CompletedQueueName);
            await queue.CreateIfNotExistsAsync(cancellationToken: ct);

            var notification = JsonSerializer.Serialize(new
            {
                correlationId = message.CorrelationId,
                documentType  = extraction.DocumentType,
                status        = extraction.PendingReview ? "pending_review" : "completed",
                processedAt   = DateTime.UtcNow,
            });
            await queue.SendMessageAsync(notification, ct);

            _logger.LogInformation(
                "Enqueued completion notification for {CorrelationId}", message.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not enqueue completion notification for {CorrelationId}", message.CorrelationId);
        }
    }
}

using System.Text.Json;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class TableStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<TableStorageService> _logger;
    private const string TableName = "ContractExtractions";

    public TableStorageService(TableServiceClient client, ILogger<TableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task WriteExtractionAsync(
        string            correlationId,
        string            blobPath,
        ExtractionResult  extraction,
        CancellationToken ct = default)
    {
        var entity = new ContractExtractionEntity
        {
            PartitionKey     = correlationId,
            RowKey           = "extraction",
            BlobPath         = blobPath,
            DocumentType     = extraction.DocumentType,
            TriageConfidence = extraction.TriageConfidence,
            Fields           = JsonSerializer.Serialize(extraction),
            ModelUsed        = extraction.ModelUsed,
            ProcessedAt      = DateTime.UtcNow,
            Status           = extraction.PendingReview ? "pending_review" : "completed",
        };

        var table = _client.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Wrote extraction record — correlationId:{CorrelationId} docType:{DocType} status:{Status}",
            correlationId, extraction.DocumentType, entity.Status);
    }
}

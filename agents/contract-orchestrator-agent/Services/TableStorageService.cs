using System.Text.Json;
using Azure.Data.Tables;
using ContractOrchestratorAgent.Models;

namespace ContractOrchestratorAgent.Services;

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
        string            modelUsed,
        CancellationToken ct = default)
    {
        var entity = new ContractExtractionEntity
        {
            PartitionKey = correlationId,
            RowKey       = "extraction",
            BlobPath     = blobPath,
            DocumentType = extraction.DocumentType,
            Fields       = JsonSerializer.Serialize(extraction),
            ModelUsed    = modelUsed,
            ProcessedAt  = DateTime.UtcNow,
            Status       = "completed",
        };

        var table = _client.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Wrote extraction record — correlationId:{CorrelationId} docType:{DocType} model:{Model}",
            correlationId, extraction.DocumentType, modelUsed);
    }
}

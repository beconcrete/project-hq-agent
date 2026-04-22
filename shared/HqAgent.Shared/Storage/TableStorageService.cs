using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class TableStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<TableStorageService> _logger;
    private const string TableName = "Contracts";

    public TableStorageService(TableServiceClient client, ILogger<TableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task WriteExtractionAsync(
        ContractMessage   message,
        ExtractionResult  extraction,
        CancellationToken ct = default)
    {
        var facts = ContractFactsExtractor.Extract(extraction);
        var entity = new ContractExtractionEntity
        {
            PartitionKey     = message.CorrelationId,
            RowKey           = "extraction",
            BlobPath         = message.BlobName,
            UserId           = message.UserId,
            FileName         = message.FileName,
            UploadedAt       = message.UploadedAt,
            DocumentType     = extraction.DocumentType,
            TriageConfidence = extraction.TriageConfidence,
            ExtractionConfidence = extraction.ExtractionConfidence,
            EffectiveDate    = facts.EffectiveDate,
            ExpiryDate       = facts.ExpiryDate,
            NoticePeriodDays = facts.NoticePeriodDays,
            NoticeDeadline   = facts.NoticeDeadline,
            AutoRenewal      = facts.AutoRenewal,
            PrimaryCounterparty = facts.PrimaryCounterparty,
            CounterpartyNames = JsonSerializer.Serialize(facts.CounterpartyNames),
            PeopleMentioned  = JsonSerializer.Serialize(facts.PeopleMentioned),
            CustomerName     = facts.CustomerName,
            AssignmentStartDate = facts.AssignmentStartDate,
            AssignmentEndDate = facts.AssignmentEndDate,
            PaymentAmount    = facts.PaymentAmount.HasValue ? (double)facts.PaymentAmount.Value : null,
            PaymentCurrency  = facts.PaymentCurrency,
            PaymentUnit      = facts.PaymentUnit,
            PaymentType      = facts.PaymentType,
            PaymentTerms     = facts.PaymentTerms,
            RiskFlags        = JsonSerializer.Serialize(facts.RiskFlags),
            MissingFields    = JsonSerializer.Serialize(facts.MissingFields),
            Fields           = JsonSerializer.Serialize(extraction),
            ModelUsed        = extraction.ModelUsed,
            ProcessedAt      = DateTime.UtcNow,
            Status           = extraction.PendingReview ? "pending_review" : "completed",
            ReviewState      = extraction.PendingReview ? "pending_review" : "approved_by_extraction",
        };

        var table = _client.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Wrote extraction record — correlationId:{CorrelationId} docType:{DocType} status:{Status}",
            message.CorrelationId, extraction.DocumentType, entity.Status);
    }

    public async Task WriteFailedAsync(
        ContractMessage   message,
        CancellationToken ct = default)
    {
        var entity = new ContractExtractionEntity
        {
            PartitionKey = message.CorrelationId,
            RowKey       = "extraction",
            BlobPath     = message.BlobName,
            UserId       = message.UserId,
            FileName     = message.FileName,
            UploadedAt   = message.UploadedAt,
            ProcessedAt  = DateTime.UtcNow,
            Status       = "failed",
            ReviewState  = "failed",
        };

        var table = _client.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Wrote failed record — correlationId:{CorrelationId}", message.CorrelationId);
    }

    public async Task<ContractExtractionEntity?> GetExtractionAsync(
        string            correlationId,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableName);
        try
        {
            var response = await table.GetEntityAsync<ContractExtractionEntity>(
                correlationId, "extraction", cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<ContractExtractionEntity>> ListExtractionsAsync(
        string?           userId = null,
        CancellationToken ct     = default)
    {
        var table = _client.GetTableClient(TableName);
        var results = new List<ContractExtractionEntity>();

        var filter = userId != null
            ? $"UserId eq '{userId}'"
            : null;

        try
        {
            await foreach (var entity in table.QueryAsync<ContractExtractionEntity>(
                filter: filter, cancellationToken: ct))
            {
                results.Add(entity);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Table does not exist yet — return empty list
        }

        results.Sort((a, b) => b.UploadedAt.CompareTo(a.UploadedAt));
        return results;
    }

    public async Task<ContractExtractionEntity?> UpdateReviewAsync(
        string            correlationId,
        string            reviewState,
        string            reviewedBy,
        string?           reviewNote,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableName);
        ContractExtractionEntity entity;
        try
        {
            entity = (await table.GetEntityAsync<ContractExtractionEntity>(
                correlationId, "extraction", cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        entity.ReviewState = reviewState;
        entity.Status = reviewState is "approved" or "approved_by_extraction"
            ? "completed"
            : "pending_review";
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewedBy = reviewedBy;
        entity.ReviewNote = reviewNote ?? "";

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation(
            "Updated contract review — correlationId:{CorrelationId} reviewState:{ReviewState} reviewedBy:{ReviewedBy}",
            correlationId, reviewState, reviewedBy);

        return entity;
    }
}

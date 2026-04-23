using System.Text.Json;
using HqAgent.Agents.Services;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;

namespace HqAgent.Agents.Contract.Services;

public class ContractIntelligence : IContractIntelligence
{
    private readonly TableStorageService _table;
    private readonly BlobStorageService _blobs;
    private readonly DocumentTextExtractor _textExtractor;

    public ContractIntelligence(
        TableStorageService table,
        BlobStorageService blobs,
        DocumentTextExtractor textExtractor)
    {
        _table = table;
        _blobs = blobs;
        _textExtractor = textExtractor;
    }

    public async Task<IReadOnlyList<ContractSummary>> ListContractsAsync(
        ContractCallerContext caller,
        CancellationToken ct)
    {
        var entities = await LoadVisibleEntitiesAsync(caller, ct);
        return entities.Select(ToSummary).ToArray();
    }

    public async Task<ContractDetail?> GetContractAsync(
        string correlationId,
        ContractCallerContext caller,
        CancellationToken ct)
    {
        var entity = await _table.GetExtractionAsync(correlationId, ct);
        if (entity is null || IsDeleted(entity) || !CanAccess(entity, caller))
            return null;

        return new ContractDetail(ToSummary(entity), entity.Fields, entity);
    }

    public async Task<string?> GetContractDocumentTextAsync(
        string correlationId,
        ContractCallerContext caller,
        CancellationToken ct)
    {
        var detail = await GetContractAsync(correlationId, caller, ct);
        if (detail is null)
            return null;

        var entity = detail.Entity;
        var (bytes, contentType) = await _blobs.DownloadAsync("contracts", entity.BlobPath, ct);
        return await _textExtractor.ExtractAsync(bytes, contentType, entity.BlobPath, ct);
    }

    public async Task<IReadOnlyList<ContractSummary>> FindExpiringAsync(
        ContractCallerContext caller,
        DateOnly? from,
        DateOnly? to,
        string? contractType,
        CancellationToken ct)
    {
        var fromDate = ToDateTime(from) ?? DateTime.UtcNow.Date;
        var toDate = ToDateTime(to) ?? fromDate.AddDays(90);

        var entities = await LoadVisibleEntitiesAsync(caller, ct);
        return entities
            .Where(e => e.ExpiryDate.HasValue)
            .Where(e => e.ExpiryDate!.Value.Date >= fromDate && e.ExpiryDate.Value.Date <= toDate)
            .Where(e => string.IsNullOrWhiteSpace(contractType) ||
                e.DocumentType.Contains(contractType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ExpiryDate)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<ContractSummary>> FindRenewalWindowsAsync(
        ContractCallerContext caller,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var fromDate = ToDateTime(from) ?? DateTime.UtcNow.Date;
        var toDate = ToDateTime(to) ?? fromDate.AddDays(90);

        var entities = await LoadVisibleEntitiesAsync(caller, ct);
        return entities
            .Where(e => (e.NoticeDeadline ?? e.ExpiryDate)?.Date is DateTime actionDate &&
                actionDate >= fromDate && actionDate <= toDate)
            .OrderBy(e => e.NoticeDeadline ?? e.ExpiryDate)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<ContractSummary>> FindByPersonAsync(
        ContractCallerContext caller,
        string personName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personName))
            return [];

        var entities = await LoadVisibleEntitiesAsync(caller, ct);
        return entities
            .Where(e => ContainsAny(ParseJsonList(e.PeopleMentioned), personName) ||
                        e.Fields.Contains(personName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ExpiryDate ?? DateTime.MaxValue)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<ContractSummary>> FindByCounterpartyAsync(
        ContractCallerContext caller,
        string counterparty,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(counterparty))
            return [];

        var entities = await LoadVisibleEntitiesAsync(caller, ct);
        return entities
            .Where(e => e.PrimaryCounterparty.Contains(counterparty, StringComparison.OrdinalIgnoreCase) ||
                        ContainsAny(ParseJsonList(e.CounterpartyNames), counterparty) ||
                        e.Fields.Contains(counterparty, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ExpiryDate ?? DateTime.MaxValue)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<ContractAnswer> AnswerAsync(ContractQuestion question, CancellationToken ct)
    {
        var text = question.Question;
        IReadOnlyList<ContractSummary> matches;

        if (text.Contains("renew", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("notice", StringComparison.OrdinalIgnoreCase))
        {
            matches = await FindRenewalWindowsAsync(question.Caller, null, null, ct);
            return new ContractAnswer("Contracts with upcoming renewal or notice windows.", matches);
        }

        if (text.Contains("expire", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ending", StringComparison.OrdinalIgnoreCase))
        {
            matches = await FindExpiringAsync(question.Caller, null, null, null, ct);
            return new ContractAnswer("Contracts expiring in the next 90 days.", matches);
        }

        matches = await ListContractsAsync(question.Caller, ct);
        return new ContractAnswer("Visible contracts for this caller.", matches);
    }

    private async Task<List<ContractExtractionEntity>> LoadVisibleEntitiesAsync(
        ContractCallerContext caller,
        CancellationToken ct) =>
        await _table.ListExtractionsAsync(caller.IsAdmin ? null : caller.UserId, ct);

    private static bool CanAccess(ContractExtractionEntity entity, ContractCallerContext caller) =>
        caller.IsAdmin || entity.UserId == caller.UserId;

    private static ContractSummary ToSummary(ContractExtractionEntity e) =>
        new(
            e.PartitionKey,
            e.FileName,
            e.Status,
            e.DocumentType,
            e.UploadedAt,
            e.EffectiveDate,
            e.ExpiryDate,
            e.NoticeDeadline,
            e.NoticePeriodDays,
            e.AutoRenewal,
            e.PrimaryCounterparty,
            ParseJsonList(e.CounterpartyNames),
            ParseJsonList(e.PeopleMentioned),
            e.CustomerName,
            e.AssignmentStartDate,
            e.AssignmentEndDate,
            e.PaymentAmount,
            e.PaymentCurrency,
            e.PaymentUnit,
            e.PaymentType,
            e.PaymentTerms,
            ParseJsonList(e.RiskFlags),
            ParseJsonList(e.MissingFields),
            string.IsNullOrWhiteSpace(e.ReviewState)
                ? (e.Status == "pending_review" ? "pending_review" : "approved_by_extraction")
                : e.ReviewState,
            e.RelationshipType,
            e.DuplicateOfCorrelationId,
            e.SupersedesCorrelationId,
            ParseJsonList(e.RelatedContractIds),
            ParseJsonList(e.RelationshipReasons));

    private static IReadOnlyList<string> ParseJsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ContainsAny(IEnumerable<string> values, string query) =>
        values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static DateTime? ToDateTime(DateOnly? date) =>
        date?.ToDateTime(TimeOnly.MinValue);

    private static bool IsDeleted(ContractExtractionEntity entity) =>
        entity.Status == "deleted" ||
        entity.ReviewState is "rejected" or "duplicate_deleted" ||
        entity.DeletedAt.HasValue;
}

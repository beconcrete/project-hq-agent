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
    public TableStorageService(TableServiceClient client, ILogger<TableStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ContractEntity> WriteExtractionAsync(
        ContractMessage   message,
        ExtractionResult  extraction,
        CancellationToken ct = default)
    {
        var facts = ContractFactsExtractor.Extract(extraction);
        var entity = new ContractEntity
        {
            PartitionKey         = "contracts",
            RowKey               = message.CorrelationId,
            BlobPath             = message.BlobName,
            UserId               = message.UserId,
            FileName             = message.FileName,
            UploadedAt           = message.UploadedAt,
            DocumentType         = extraction.DocumentType,
            TriageConfidence     = extraction.TriageConfidence,
            ExtractionConfidence = extraction.ExtractionConfidence,
            EffectiveDate        = facts.EffectiveDate,
            ExpiryDate           = facts.ExpiryDate,
            NoticePeriodDays     = facts.NoticePeriodDays,
            NoticeDeadline       = facts.NoticeDeadline,
            AutoRenewal          = facts.AutoRenewal,
            PrimaryCounterparty  = facts.PrimaryCounterparty,
            CounterpartyNames    = JsonSerializer.Serialize(facts.CounterpartyNames),
            PeopleMentioned      = JsonSerializer.Serialize(facts.PeopleMentioned),
            CustomerName         = facts.CustomerName,
            AssignmentStartDate  = facts.AssignmentStartDate,
            AssignmentEndDate    = facts.AssignmentEndDate,
            PaymentAmount        = facts.PaymentAmount.HasValue ? (double)facts.PaymentAmount.Value : null,
            PaymentCurrency      = facts.PaymentCurrency,
            PaymentUnit          = facts.PaymentUnit,
            PaymentType          = facts.PaymentType,
            PaymentTerms         = facts.PaymentTerms,
            RiskFlags            = JsonSerializer.Serialize(facts.RiskFlags),
            MissingFields        = JsonSerializer.Serialize(facts.MissingFields),
            Fields               = JsonSerializer.Serialize(extraction),
            ModelUsed            = extraction.ModelUsed,
            ProcessedAt          = DateTime.UtcNow,
            Status               = extraction.PendingReview ? "pending_review" : "completed",
            StatusMessage        = extraction.PendingReview ? "Needs review before approval." : "Extraction completed.",
            LastError            = string.Empty,
            RetryCount           = null,
            ReviewState          = extraction.PendingReview ? "pending_review" : "approved_by_extraction",
        };

        var table = _client.GetTableClient(TableNames.Contracts);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        var candidates = await FindRelationshipCandidatesAsync(table, entity, ct);
        var primaryCandidate = candidates.FirstOrDefault();
        entity.RelationshipType = primaryCandidate?.RelationshipType ?? "new";
        entity.RelationshipReasons = JsonSerializer.Serialize(
            primaryCandidate?.Reasons ?? Array.Empty<string>());
        entity.RelationshipCandidates = JsonSerializer.Serialize(candidates);
        if (primaryCandidate?.RelationshipType == "duplicate")
            entity.DuplicateOfCorrelationId = primaryCandidate.ContractId;
        else if (primaryCandidate?.RelationshipType == "replacement")
            entity.SupersedesCorrelationId = primaryCandidate.ContractId;
        else if (primaryCandidate?.RelationshipType == "extension")
            entity.RelatedContractIds = JsonSerializer.Serialize(new[] { primaryCandidate.ContractId });

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Wrote extraction record — contractId:{ContractId} docType:{DocType} status:{Status}",
            message.CorrelationId, extraction.DocumentType, entity.Status);

        return entity;
    }

    public async Task UpdateLinkedCustomersAsync(
        string              contractId,
        IEnumerable<string> customerIds,
        IEnumerable<string> customerNames,
        CancellationToken   ct = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        try
        {
            var entity = (await table.GetEntityAsync<ContractEntity>(
                "contracts", contractId, cancellationToken: ct)).Value;
            entity.LinkedCustomerIds   = JsonSerializer.Serialize(customerIds.ToList());
            entity.LinkedCustomerNames = JsonSerializer.Serialize(customerNames.ToList());
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            _logger.LogInformation("Updated LinkedCustomerIds on contract {ContractId}: {Ids}",
                contractId, entity.LinkedCustomerIds);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Contract {ContractId} not found when updating linked customers", contractId);
        }
    }

    public async Task WriteProcessingAsync(
        ContractMessage   message,
        string            statusMessage,
        string?           lastError  = null,
        int?              retryCount = null,
        CancellationToken ct         = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        ContractEntity entity;
        try
        {
            entity = (await table.GetEntityAsync<ContractEntity>(
                "contracts", message.CorrelationId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            entity = new ContractEntity
            {
                PartitionKey = "contracts",
                RowKey       = message.CorrelationId,
                BlobPath     = message.BlobName,
                UserId       = message.UserId,
                FileName     = message.FileName,
                UploadedAt   = message.UploadedAt,
            };
        }

        entity.BlobPath      = message.BlobName;
        entity.UserId        = message.UserId;
        entity.FileName      = message.FileName;
        entity.UploadedAt    = message.UploadedAt;
        entity.ProcessedAt   = DateTime.UtcNow;
        entity.Status        = "processing";
        entity.StatusMessage = statusMessage;
        entity.ReviewState   = string.Empty;
        entity.RetryCount    = retryCount;
        if (!string.IsNullOrWhiteSpace(lastError))
            entity.LastError = Truncate(lastError, 2048);

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Updated processing record — contractId:{ContractId} status:{StatusMessage} retryCount:{RetryCount}",
            message.CorrelationId, statusMessage, retryCount);
    }

    public async Task WriteFailedAsync(
        ContractMessage   message,
        string?           errorMessage = null,
        int?              retryCount   = null,
        CancellationToken ct           = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        ContractEntity entity;
        try
        {
            entity = (await table.GetEntityAsync<ContractEntity>(
                "contracts", message.CorrelationId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            entity = new ContractEntity
            {
                PartitionKey = "contracts",
                RowKey       = message.CorrelationId,
                BlobPath     = message.BlobName,
                UserId       = message.UserId,
                FileName     = message.FileName,
                UploadedAt   = message.UploadedAt,
            };
        }

        entity.BlobPath      = message.BlobName;
        entity.UserId        = message.UserId;
        entity.FileName      = message.FileName;
        entity.UploadedAt    = message.UploadedAt;
        entity.ProcessedAt   = DateTime.UtcNow;
        entity.Status        = "failed";
        entity.StatusMessage = "Extraction failed.";
        entity.ReviewState   = "failed";
        entity.RetryCount    = retryCount ?? entity.RetryCount;
        entity.LastError     = Truncate(
            string.IsNullOrWhiteSpace(errorMessage) ? entity.LastError : errorMessage,
            2048);

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation("Wrote failed record — contractId:{ContractId}", message.CorrelationId);
    }

    public async Task<ContractEntity?> GetExtractionAsync(
        string            contractId,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        try
        {
            var response = await table.GetEntityAsync<ContractEntity>(
                "contracts", contractId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<ContractEntity>> ListExtractionsAsync(
        string?           userId        = null,
        CancellationToken ct            = default,
        bool              includeDeleted = false)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        var results = new List<ContractEntity>();

        var filter = userId != null
            ? $"PartitionKey eq 'contracts' and UserId eq '{userId}'"
            : "PartitionKey eq 'contracts'";

        try
        {
            await foreach (var entity in table.QueryAsync<ContractEntity>(
                filter: filter, cancellationToken: ct))
            {
                if (includeDeleted || !IsDeleted(entity))
                    results.Add(entity);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        results.Sort((a, b) => b.UploadedAt.CompareTo(a.UploadedAt));
        return results;
    }

    public async Task<ContractEntity?> UpdateReviewAsync(
        string            contractId,
        string            reviewState,
        string            reviewedBy,
        string?           reviewNote,
        string?           relationshipType     = null,
        string?           relatedContractId    = null,
        CancellationToken ct                   = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        ContractEntity entity;
        try
        {
            entity = (await table.GetEntityAsync<ContractEntity>(
                "contracts", contractId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        entity.ReviewState = reviewState;
        entity.Status      = reviewState is "rejected" or "duplicate_deleted"
            ? "deleted"
            : reviewState is "approved" or "approved_by_extraction"
                ? "completed"
                : "pending_review";
        entity.ReviewedAt  = DateTime.UtcNow;
        entity.ReviewedBy  = reviewedBy;
        entity.ReviewNote  = reviewNote ?? "";
        if (!string.IsNullOrWhiteSpace(relationshipType))
            entity.RelationshipType = relationshipType;
        if (!string.IsNullOrWhiteSpace(relatedContractId))
        {
            if (entity.RelationshipType == "duplicate")
                entity.DuplicateOfCorrelationId = relatedContractId;
            else if (entity.RelationshipType == "replacement")
                entity.SupersedesCorrelationId = relatedContractId;
            else if (entity.RelationshipType == "extension")
                entity.RelatedContractIds = JsonSerializer.Serialize(new[] { relatedContractId });
        }
        if (entity.Status == "deleted")
        {
            entity.DeletedAt     = DateTime.UtcNow;
            entity.DeletedBy     = reviewedBy;
            entity.DeleteReason  = reviewNote ?? reviewState;
        }

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation(
            "Updated contract review — contractId:{ContractId} reviewState:{ReviewState} reviewedBy:{ReviewedBy}",
            contractId, reviewState, reviewedBy);

        return entity;
    }

    public async Task<ContractEntity?> UpdatePartyOverrideAsync(
        string            contractId,
        string            party,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        ContractEntity entity;
        try
        {
            entity = (await table.GetEntityAsync<ContractEntity>(
                "contracts", contractId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        entity.ManualPartyOverride = party;
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Updated party override — contractId:{ContractId} party:{Party}", contractId, party);

        return entity;
    }

    public async Task<bool> HardDeleteContractAsync(
        string            contractId,
        CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Contracts);
        try
        {
            await table.DeleteEntityAsync("contracts", contractId, cancellationToken: ct);
            _logger.LogInformation("Hard-deleted contract — contractId:{ContractId}", contractId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private static bool IsDeleted(ContractEntity entity) =>
        entity.Status == "deleted" ||
        entity.ReviewState is "rejected" or "duplicate_deleted" ||
        entity.DeletedAt.HasValue;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static async Task<IReadOnlyList<ContractRelationshipCandidate>> FindRelationshipCandidatesAsync(
        TableClient table,
        ContractEntity entity,
        CancellationToken ct)
    {
        var candidates = new List<ContractRelationshipCandidate>();

        await foreach (var existing in table.QueryAsync<ContractEntity>(
            filter: "PartitionKey eq 'contracts'", cancellationToken: ct))
        {
            if (existing.RowKey == entity.RowKey || IsDeleted(existing))
                continue;

            var candidate = ClassifyRelationship(entity, existing);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.RelationshipType == "duplicate" ? 0 : c.RelationshipType == "replacement" ? 1 : 2)
            .Take(5)
            .ToArray();
    }

    private static ContractRelationshipCandidate? ClassifyRelationship(
        ContractEntity current,
        ContractEntity existing)
    {
        var reasons = new List<string>();
        var score   = 0;

        var sameFamily      = SameFamily(current.DocumentType, existing.DocumentType);
        var sameCounterparty = SameCounterparty(current, existing);
        var samePeople      = Overlaps(ParseJsonList(current.PeopleMentioned), ParseJsonList(existing.PeopleMentioned));

        if (!sameCounterparty)
            return null;

        if (sameFamily)
        {
            score += 2;
            reasons.Add("Same contract family.");
        }

        score += 3;
        reasons.Add("Same customer or counterparty.");

        if (samePeople)
        {
            score += 3;
            reasons.Add("Same consultant, employee, contact, or signatory.");
        }

        var currentStart        = current.AssignmentStartDate  ?? current.EffectiveDate;
        var currentEnd          = current.AssignmentEndDate    ?? current.ExpiryDate;
        var existingStart       = existing.AssignmentStartDate ?? existing.EffectiveDate;
        var existingEnd         = existing.AssignmentEndDate   ?? existing.ExpiryDate;
        var sameDates           = SameDate(currentStart, existingStart) && SameDate(currentEnd, existingEnd);
        var overlappingDates    = DatesOverlap(currentStart, currentEnd, existingStart, existingEnd);
        var laterNonOverlapping = currentStart.HasValue && existingEnd.HasValue && currentStart.Value.Date > existingEnd.Value.Date;

        if (sameDates) { score += 4; reasons.Add("Same start and end dates."); }
        else if (overlappingDates) { score += 2; reasons.Add("Overlapping dates."); }
        else if (laterNonOverlapping) { score += 2; reasons.Add("New later date period."); }

        if (SamePayment(current, existing)) { score += 1; reasons.Add("Similar payment facts."); }
        else if (PaymentChanged(current, existing)) { score += 1; reasons.Add("Payment, rate, or terms differ."); }

        if (score < 6)
            return null;

        var extendsExistingPeriod =
            samePeople &&
            currentEnd.HasValue && existingEnd.HasValue &&
            currentEnd.Value.Date > existingEnd.Value.Date &&
            currentStart.HasValue && existingStart.HasValue &&
            currentStart.Value.Date <= existingEnd.Value.Date;

        var isExplicitExtension =
            ContainsAny(current.DocumentType, "extension", "amendment") ||
            ContainsAny(existing.DocumentType, "extension", "amendment");

        var relationshipType = sameFamily && sameDates && (samePeople || !HasPeople(current, existing))
            ? "duplicate"
            : sameFamily && samePeople && (laterNonOverlapping || extendsExistingPeriod || isExplicitExtension)
                ? "extension"
                : sameFamily && samePeople && overlappingDates
                    ? "replacement"
                    : "unknown";

        if (relationshipType == "unknown")
            return null;

        return new ContractRelationshipCandidate(
            existing.RowKey,
            existing.FileName,
            existing.DocumentType,
            relationshipType,
            score,
            reasons);
    }

    private static bool SameFamily(string left, string right)
    {
        var l = Family(left);
        var r = Family(right);
        return l.Length > 0 && l == r;
    }

    private static string Family(string documentType)
    {
        var normalized = documentType.ToLowerInvariant();
        if (normalized.Contains("nda") || normalized.Contains("non-disclosure") || normalized.Contains("confidential"))
            return "nda";
        if (normalized.Contains("consult"))
            return "consulting";
        if (normalized.Contains("license") || normalized.Contains("licence") || normalized.Contains("software"))
            return "software_license";
        if (normalized.Contains("service") || normalized.Contains("customer"))
            return "service";
        return normalized.Trim();
    }

    private static bool SameCounterparty(ContractEntity left, ContractEntity right)
    {
        var leftNames  = ParseJsonList(left.CounterpartyNames)
            .Append(left.PrimaryCounterparty).Append(left.CustomerName)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Where(s => !IsOwnCompany(s));
        var rightNames = ParseJsonList(right.CounterpartyNames)
            .Append(right.PrimaryCounterparty).Append(right.CustomerName)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Where(s => !IsOwnCompany(s));
        return Overlaps(leftNames, rightNames);
    }

    private static bool IsOwnCompany(string value)
    {
        var normalized = Normalize(value);
        return normalized is "be concrete ab" or "be concrete";
    }

    private static bool HasPeople(ContractEntity left, ContractEntity right) =>
        ParseJsonList(left.PeopleMentioned).Count > 0 || ParseJsonList(right.PeopleMentioned).Count > 0;

    private static bool Overlaps(IEnumerable<string> left, IEnumerable<string> right)
    {
        var normalizedRight = right.Select(Normalize).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return left.Select(Normalize).Where(s => s.Length > 0).Any(normalizedRight.Contains);
    }

    private static bool SameDate(DateTime? left, DateTime? right) =>
        left.HasValue && right.HasValue && left.Value.Date == right.Value.Date;

    private static bool DatesOverlap(DateTime? ls, DateTime? le, DateTime? rs, DateTime? re)
    {
        if (!ls.HasValue || !le.HasValue || !rs.HasValue || !re.HasValue)
            return false;
        return ls.Value.Date <= re.Value.Date && rs.Value.Date <= le.Value.Date;
    }

    private static bool SamePayment(ContractEntity left, ContractEntity right) =>
        left.PaymentAmount.HasValue && right.PaymentAmount.HasValue &&
        Math.Abs(left.PaymentAmount.Value - right.PaymentAmount.Value) < 0.01 &&
        string.Equals(left.PaymentCurrency, right.PaymentCurrency, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.PaymentUnit, right.PaymentUnit, StringComparison.OrdinalIgnoreCase);

    private static bool PaymentChanged(ContractEntity left, ContractEntity right) =>
        left.PaymentAmount.HasValue && right.PaymentAmount.HasValue &&
        Math.Abs(left.PaymentAmount.Value - right.PaymentAmount.Value) >= 0.01;

    private static IReadOnlyList<string> ParseJsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Normalize(string value) =>
        string.Join(" ", value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public record ContractRelationshipCandidate(
    string ContractId,
    string FileName,
    string DocumentType,
    string RelationshipType,
    int Score,
    IReadOnlyList<string> Reasons);

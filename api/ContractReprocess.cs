using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Requeues a failed contract for reprocessing without creating a duplicate record.
/// POST /api/contract-reprocess
/// Body: { "rowKey": "correlationId" }
/// Resets status to "processing", sends a new queue message with the same correlationId.
/// An optional hint derived from the previous LastError is included to help the agent succeed.
/// Requires admin role.
/// </summary>
public class ContractReprocess
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;
    private readonly string _storageConnectionString;

    public ContractReprocess(
        IHttpClientFactory httpFactory,
        TableStorageService table,
        IConfiguration config)
    {
        _httpFactory             = httpFactory;
        _table                   = table;
        _appId                   = config["APP_ID"] ?? "hqagents";
        _storageConnectionString = config["STORAGE_CONNECTION_STRING"]
            ?? config["AzureWebJobsStorage"]
            ?? "";
    }

    [Function("ContractReprocess")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "contract-reprocess")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        ContractReprocessRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ContractReprocessRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Invalid JSON body");
        }

        if (string.IsNullOrWhiteSpace(body?.RowKey))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "rowKey is required");

        var entity = await _table.GetExtractionAsync(body.RowKey, context.CancellationToken);
        if (entity is null)
            return await PlainResponse(req, HttpStatusCode.NotFound, "Contract not found");

        if (string.IsNullOrWhiteSpace(entity.BlobPath))
            return await PlainResponse(req, HttpStatusCode.UnprocessableEntity, "Contract has no associated blob and cannot be reprocessed");

        var hint = BuildHint(entity.LastError, entity.DocumentType);

        var msg = new ContractMessage(
            BlobName:       entity.BlobPath,
            CorrelationId:  body.RowKey,
            UploadedAt:     DateTime.UtcNow,
            ContainerName:  "contracts",
            UserId:         entity.UserId ?? "",
            FileName:       entity.FileName ?? "",
            ProcessingHint: hint);

        // Reset the table record to "processing" before sending to the queue
        await _table.WriteProcessingAsync(msg, "Reprocessing requested.", ct: context.CancellationToken);

        var queueClient = new QueueServiceClient(_storageConnectionString)
            .GetQueueClient("contract-processing");
        await queueClient.CreateIfNotExistsAsync();

        var messageJson = JsonSerializer.Serialize(msg);
        await queueClient.SendMessageAsync(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(messageJson)));

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { rowKey = body.RowKey, status = "processing" });
        res.StatusCode = HttpStatusCode.Accepted;
        return res;
    }

    private static string BuildHint(string? lastError, string? documentType)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(documentType))
            parts.Add($"Previous triage classified this as: {documentType}.");

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            var truncated = lastError.Length > 400 ? lastError[..400] + "…" : lastError;
            parts.Add($"Previous processing failed with: {truncated}");
        }

        if (parts.Count > 0)
            parts.Add("Please extract all fields carefully, set pendingReview to true if uncertain.");

        return string.Join(" ", parts);
    }

    private static async Task<HttpResponseData> PlainResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }
}

public record ContractReprocessRequest(string RowKey);

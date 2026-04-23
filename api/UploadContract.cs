using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using HqAgent.Shared.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;

namespace HqAgent.Api;

/// <summary>
/// Accepts a contract file upload and writes it to Azure Blob Storage.
/// POST /api/upload-contract — multipart/form-data with a single file field.
/// Returns { correlationId, blobName, fileName, status: "processing" }.
/// Requires the admin role. App access is enforced by RequireAccessMiddleware.
/// </summary>
public class UploadContract
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx" };

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _appId;
    private readonly string _storageConnectionString;

    public UploadContract(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _appId = config["APP_ID"] ?? "hqagents";
        _storageConnectionString = config["STORAGE_CONNECTION_STRING"]
            ?? config["AzureWebJobsStorage"]
            ?? "";
    }

    [Function("UploadContract")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-contract")] HttpRequestData req,
        FunctionContext context)
    {
        // Role check — admin only
        RoleGuard guard;
        try
        {
            guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin);
        }
        catch
        {
            return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable");
        }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() ?? "" : "";

        // Parse multipart boundary
        var contentType = req.Headers.TryGetValues("Content-Type", out var ctVals)
            ? ctVals.FirstOrDefault() ?? ""
            : "";

        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Request must be multipart/form-data");

        string boundary;
        try
        {
            var mediaType = MediaTypeHeaderValue.Parse(contentType);
            boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value
                ?? throw new InvalidOperationException("Missing boundary");
        }
        catch
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Invalid multipart boundary");
        }

        // Read the file section
        string? fileName = null;
        string? fileContentType = null;
        byte[]? fileBytes = null;

        try
        {
            var reader = new MultipartReader(boundary, req.Body) { BodyLengthLimit = MaxFileSizeBytes };
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
                    continue;
                if (!cd.IsFileDisposition())
                    continue;

                fileName = cd.FileName.Value?.Trim('"') ?? "contract";
                fileContentType = section.ContentType ?? "application/octet-stream";

                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms);
                fileBytes = ms.ToArray();
                break;
            }
        }
        catch (InvalidDataException)
        {
            return await PlainResponse(req, HttpStatusCode.BadRequest, "File exceeds the 20 MB size limit");
        }

        if (fileBytes is null || fileName is null)
            return await PlainResponse(req, HttpStatusCode.BadRequest, "No file found in request");

        var ext = ResolveFileExtension(fileName, fileContentType);
        if (ext is null)
            return await PlainResponse(req, HttpStatusCode.BadRequest, "Only PDF and DOCX files are accepted");

        // Upload to blob storage with a safe internal blob name while preserving the original filename elsewhere.
        var correlationId = Guid.NewGuid().ToString();
        var blobName = $"{correlationId}/document{ext}";

        var containerClient = new BlobServiceClient(_storageConnectionString)
            .GetBlobContainerClient("contracts");

        await containerClient.CreateIfNotExistsAsync();

        await containerClient.GetBlobClient(blobName).UploadAsync(
            new BinaryData(fileBytes),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = fileContentType },
                Metadata = new Dictionary<string, string>
                {
                    { "userId", userId },
                    { "correlationId", correlationId },
                    { "originalFileNameBase64", Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName)) }
                }
            });

        var queueClient = new QueueServiceClient(_storageConnectionString)
            .GetQueueClient("contract-processing");
        await queueClient.CreateIfNotExistsAsync();

        var message = new ContractMessage(
            BlobName: blobName,
            CorrelationId: correlationId,
            UploadedAt: DateTime.UtcNow,
            ContainerName: "contracts",
            UserId: userId,
            FileName: fileName);

        var messageJson = JsonSerializer.Serialize(message);
        await queueClient.SendMessageAsync(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageJson)));

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { correlationId, blobName, fileName, status = "processing" });
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static string? ResolveFileExtension(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (AllowedExtensions.Contains(ext))
            return ext.ToLowerInvariant();

        return contentType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            _ => null
        };
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

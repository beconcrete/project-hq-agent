using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class BlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(BlobServiceClient client, ILogger<BlobStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<(byte[] Data, string ContentType)> DownloadAsync(
        string            containerName,
        string            blobName,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading blob {Container}/{Blob}", containerName, blobName);

        var blob     = _client.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var download = await blob.DownloadContentAsync(ct);
        var bytes    = download.Value.Content.ToArray();
        var ct_      = download.Value.Details.ContentType ?? "application/pdf";

        _logger.LogInformation("Downloaded {Size} bytes (content-type: {ContentType})", bytes.Length, ct_);
        return (bytes, ct_);
    }
}

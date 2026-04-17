using Azure.Storage.Blobs;

namespace ContractOrchestratorAgent.Services;

public class BlobDownloadService
{
    private readonly BlobServiceClient _client;
    private readonly ILogger<BlobDownloadService> _logger;

    public BlobDownloadService(BlobServiceClient client, ILogger<BlobDownloadService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Downloads a blob and returns its raw bytes and content-type.
    /// </summary>
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

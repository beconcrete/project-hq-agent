using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HqAgent.Functions;

/// <summary>
/// Triggered when a new contract file is uploaded to Blob Storage.
/// Enqueues a processing message on the contract-processing queue.
/// </summary>
public class ContractBlobTrigger
{
    private const string ContainerName = "contracts";

    private readonly ILogger<ContractBlobTrigger> _logger;

    public ContractBlobTrigger(ILogger<ContractBlobTrigger> logger)
    {
        _logger = logger;
    }

    [Function(nameof(ContractBlobTrigger))]
    [QueueOutput("contract-processing", Connection = "STORAGE_CONNECTION_STRING")]
    public string Run(
        [BlobTrigger("contracts/{correlationId}/{name}", Connection = "STORAGE_CONNECTION_STRING")] byte[] contractBlob,
        string correlationId,
        string name)
    {
        _logger.LogInformation(
            "New contract uploaded: {CorrelationId}/{Name} ({Size} bytes)",
            correlationId, name, contractBlob.Length);

        var message = BuildMessage(correlationId, name, ContainerName);

        _logger.LogInformation(
            "Enqueued processing message for correlationId: {CorrelationId}", correlationId);

        return message;
    }

    public static string BuildMessage(string correlationId, string fileName, string containerName)
    {
        return JsonSerializer.Serialize(new
        {
            blobName = $"{correlationId}/{fileName}",
            correlationId,
            uploadedAt = DateTime.UtcNow,
            containerName,
        });
    }
}

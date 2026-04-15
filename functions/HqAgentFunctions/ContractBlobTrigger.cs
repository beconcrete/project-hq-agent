using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HqAgent.Functions;

/// <summary>
/// Triggered when a new contract file is uploaded to Blob Storage.
/// Enqueues a processing message on the contract-processing queue.
/// </summary>
public class ContractBlobTrigger
{
    private readonly ILogger<ContractBlobTrigger> _logger;

    public ContractBlobTrigger(ILogger<ContractBlobTrigger> logger)
    {
        _logger = logger;
    }

    [Function(nameof(ContractBlobTrigger))]
    [QueueOutput("contract-processing", Connection = "AzureWebJobsStorage")]
    public string Run(
        [BlobTrigger("contracts/{name}", Connection = "AzureWebJobsStorage")] byte[] contractBlob,
        string name)
    {
        _logger.LogInformation("New contract uploaded: {Name} ({Size} bytes)", name, contractBlob.Length);

        // Enqueue a processing message with the blob name
        // The Contract Orchestrator Agent will pick this up via Dapr bindings
        var message = System.Text.Json.JsonSerializer.Serialize(new
        {
            blobName = name,
            uploadedAt = DateTime.UtcNow,
            correlationId = Guid.NewGuid().ToString()
        });

        _logger.LogInformation("Enqueued processing message for contract: {Name}", name);
        return message;
    }
}

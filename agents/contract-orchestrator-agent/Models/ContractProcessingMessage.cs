using System.Text.Json.Serialization;

namespace ContractOrchestratorAgent.Models;

/// <summary>
/// Matches the JSON produced by ContractBlobTrigger.BuildMessage() in the Functions app.
/// </summary>
public record ContractProcessingMessage(
    [property: JsonPropertyName("blobName")]      string BlobName,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("uploadedAt")]    DateTime UploadedAt,
    [property: JsonPropertyName("containerName")] string ContainerName
);

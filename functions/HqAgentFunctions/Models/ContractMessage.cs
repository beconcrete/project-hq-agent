using System.Text.Json.Serialization;

namespace HqAgent.Functions.Models;

public record ContractMessage(
    [property: JsonPropertyName("blobName")] string BlobName,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("uploadedAt")] DateTime UploadedAt,
    [property: JsonPropertyName("containerName")] string ContainerName
);

using System.Text.Json.Serialization;

namespace HqAgent.Shared.Models;

/// <summary>
/// Queue message written by ContractBlobTrigger and consumed by ContractIngestion.
/// </summary>
public record ContractMessage(
    [property: JsonPropertyName("blobName")]       string   BlobName,
    [property: JsonPropertyName("correlationId")]  string   CorrelationId,
    [property: JsonPropertyName("uploadedAt")]     DateTime UploadedAt,
    [property: JsonPropertyName("containerName")]  string   ContainerName,
    [property: JsonPropertyName("userId")]         string   UserId   = "",
    [property: JsonPropertyName("fileName")]       string   FileName = ""
);

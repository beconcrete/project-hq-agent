using Azure.Data.Tables;
using HqAgent.Functions.Models;
using System.Text.Json;

namespace HqAgent.Functions.Services;

public class ExtractionTableWriter
{
    private readonly TableClient _table;

    public ExtractionTableWriter(TableClient table)
    {
        _table = table;
    }

    public async Task WriteAsync(
        ContractMessage msg,
        ExtractionResult extraction,
        string rawJson,
        string modelUsed,
        CancellationToken ct = default)
    {
        var entity = new TableEntity(msg.CorrelationId, "v1")
        {
            ["BlobPath"] = msg.BlobName,
            ["DocumentType"] = extraction.DocumentType,
            ["ProcessedAt"] = DateTimeOffset.UtcNow,
            ["ModelUsed"] = modelUsed,
            ["Status"] = "extracted",
            ["Fields"] = rawJson
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }
}

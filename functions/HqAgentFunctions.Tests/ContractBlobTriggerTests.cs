using System.Text.Json;
using HqAgent.Functions;
using Xunit;

namespace HqAgent.Functions.Tests;

public class ContractBlobTriggerTests
{
    [Fact]
    public void BuildMessage_SetsCorrectBlobName()
    {
        var msg = Deserialize(ContractBlobTrigger.BuildMessage("abc-123", "contract.pdf", "contracts"));
        Assert.Equal("abc-123/contract.pdf", msg.GetProperty("blobName").GetString());
    }

    [Fact]
    public void BuildMessage_SetsCorrelationId()
    {
        var msg = Deserialize(ContractBlobTrigger.BuildMessage("abc-123", "contract.pdf", "contracts"));
        Assert.Equal("abc-123", msg.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void BuildMessage_SetsContainerName()
    {
        var msg = Deserialize(ContractBlobTrigger.BuildMessage("abc-123", "contract.pdf", "contracts"));
        Assert.Equal("contracts", msg.GetProperty("containerName").GetString());
    }

    [Fact]
    public void BuildMessage_SetsUploadedAt_AsUtcTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var msg = Deserialize(ContractBlobTrigger.BuildMessage("abc-123", "contract.pdf", "contracts"));
        var after = DateTime.UtcNow.AddSeconds(1);

        var uploadedAt = msg.GetProperty("uploadedAt").GetDateTime();
        Assert.InRange(uploadedAt, before, after);
    }

    [Fact]
    public void BuildMessage_ProducesValidJson()
    {
        var json = ContractBlobTrigger.BuildMessage("abc-123", "contract.pdf", "contracts");
        // Throws if invalid JSON
        JsonDocument.Parse(json).Dispose();
    }

    private static JsonElement Deserialize(string json) =>
        JsonDocument.Parse(json).RootElement;
}

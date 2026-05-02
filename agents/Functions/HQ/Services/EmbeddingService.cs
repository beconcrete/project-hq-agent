using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;

namespace HqAgent.Agents.HQ.Services;

public class EmbeddingService
{
    private const string Model = "text-embedding-3-small";
    private readonly EmbeddingClient _client;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(IConfiguration config, ILogger<EmbeddingService> logger)
    {
        var apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
        _client = new OpenAIClient(apiKey).GetEmbeddingClient(Model);
        _logger = logger;
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        _logger.LogDebug("Generated embedding for text of length {Len}", text.Length);
        return result.Value.ToFloats().ToArray();
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HqAgent.Shared.Abstractions;

namespace ContractOrchestratorAgent.Services;

/// <summary>
/// Thin HTTP wrapper for the Anthropic Messages API.
/// Supports prompt caching (system prompt), PDF document input, and tool use.
/// Using HttpClient directly avoids SDK version churn and gives full control
/// over beta headers required for PDF support and prompt caching.
/// </summary>
public class AnthropicHttpClient : IAIModelClient
{
    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly ILogger<AnthropicHttpClient> _logger;

    private const string MessagesUrl      = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string BetaHeaders      = "pdfs-2024-09-25,prompt-caching-2024-07-31";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AnthropicHttpClient(
        HttpClient                    http,
        IConfiguration                config,
        ILogger<AnthropicHttpClient>  logger)
    {
        _http   = http;
        _logger = logger;
        _apiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");
    }

    /// <summary>
    /// Send a PDF document to Claude with a single tool definition and return the
    /// deserialised tool-use input as <typeparamref name="T"/>.
    /// The system prompt is marked ephemeral for prompt caching.
    /// </summary>
    public async Task<T> InvokeToolAsync<T>(
        string            model,
        string            systemPrompt,
        byte[]            documentBytes,
        string            mediaType,
        string            toolName,
        string            toolDescription,
        object            toolInputSchema,
        int               maxTokens = 1024,
        CancellationToken ct        = default)
    {
        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            system = new[]
            {
                new
                {
                    type          = "text",
                    text          = systemPrompt,
                    cache_control = new { type = "ephemeral" },
                }
            },
            tools = new[]
            {
                new
                {
                    name         = toolName,
                    description  = toolDescription,
                    input_schema = toolInputSchema,
                }
            },
            tool_choice = new { type = "tool", name = toolName },
            messages = new[]
            {
                new
                {
                    role    = "user",
                    content = new[]
                    {
                        new
                        {
                            type   = "document",
                            source = new
                            {
                                type       = "base64",
                                media_type = mediaType,
                                data       = Convert.ToBase64String(documentBytes),
                            },
                        }
                    }
                }
            },
        };

        var json    = JsonSerializer.Serialize(requestBody, SerializeOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key",         _apiKey);
        request.Headers.Add("anthropic-version",  AnthropicVersion);
        request.Headers.Add("anthropic-beta",     BetaHeaders);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Claude request — model:{Model} tool:{Tool} bytes:{Bytes}",
            model, toolName, documentBytes.Length);

        var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Claude API error {Status}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Claude API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc    = JsonDocument.Parse(responseJson);

        LogUsage(doc.RootElement, model);

        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_use")
            {
                var inputJson = block.GetProperty("input").GetRawText();
                return JsonSerializer.Deserialize<T>(inputJson, DeserializeOptions)
                    ?? throw new InvalidOperationException(
                           $"Failed to deserialise tool input for '{toolName}'");
            }
        }

        throw new InvalidOperationException(
            $"Claude did not return a tool_use block for '{toolName}'");
    }

    private void LogUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usage)) return;

        int inputTokens       = usage.TryGetProperty("input_tokens",                  out var it)  ? it.GetInt32()  : 0;
        int outputTokens      = usage.TryGetProperty("output_tokens",                 out var ot)  ? ot.GetInt32()  : 0;
        int cacheCreation     = usage.TryGetProperty("cache_creation_input_tokens",   out var cct) ? cct.GetInt32() : 0;
        int cacheRead         = usage.TryGetProperty("cache_read_input_tokens",       out var crt) ? crt.GetInt32() : 0;

        _logger.LogInformation(
            "Claude usage [{Model}] — input:{Input} output:{Output} cache_create:{Create} cache_read:{Read}",
            model, inputTokens, outputTokens, cacheCreation, cacheRead);
    }
}

namespace HqAgent.Shared.Abstractions;

public interface IAIModelClient
{
    Task<T> InvokeToolAsync<T>(
        string            model,
        string            systemPrompt,
        byte[]            documentBytes,
        string            mediaType,
        string            toolName,
        string            toolDescription,
        object            toolInputSchema,
        int               maxTokens = 1024,
        CancellationToken ct        = default);
}

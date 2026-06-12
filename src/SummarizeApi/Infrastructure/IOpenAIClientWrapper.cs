namespace SummarizeApi.Infrastructure;

/// <summary>
/// Thin abstraction over the Azure OpenAI chat client so that
/// <see cref="Services.SummarizationService"/> can be unit-tested without
/// network calls. Implementations must throw
/// <see cref="UpstreamServiceException"/> when the upstream service fails
/// after retries.
/// </summary>
public interface IOpenAIClientWrapper
{
    /// <summary>
    /// Sends a system + user prompt pair to the chat model and returns the
    /// completion text.
    /// </summary>
    Task<string> CompleteChatAsync(
        string systemPrompt,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken);
}

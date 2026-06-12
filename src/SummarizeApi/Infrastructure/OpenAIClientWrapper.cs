using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using SummarizeApi.Options;

namespace SummarizeApi.Infrastructure;

/// <summary>
/// Production implementation of <see cref="IOpenAIClientWrapper"/> backed by
/// the official Azure.AI.OpenAI SDK.
///
/// Retry strategy: the SDK's System.ClientModel pipeline already retries
/// transient failures (408/429/500/502/503/504) with exponential backoff and
/// honors Retry-After headers; the retry count is raised in Program.cs. By the
/// time an exception reaches this class the retries are exhausted, so we
/// translate it into <see cref="UpstreamServiceException"/> which the global
/// exception handler maps to 502.
/// </summary>
public sealed class OpenAIClientWrapper : IOpenAIClientWrapper
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIClientWrapper> _logger;

    public OpenAIClientWrapper(
        AzureOpenAIClient azureClient,
        IOptions<AzureOpenAIOptions> options,
        ILogger<OpenAIClientWrapper> logger)
    {
        _chatClient = azureClient.GetChatClient(options.Value.DeploymentName);
        _logger = logger;
    }

    public async Task<string> CompleteChatAsync(
        string systemPrompt,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var chatOptions = new ChatCompletionOptions
        {
            // Greedy decoding: same input -> same summary, no creative drift.
            Temperature = 0,
            MaxOutputTokenCount = maxOutputTokens,
        };

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };

        try
        {
            ChatCompletion completion = await _chatClient.CompleteChatAsync(
                messages, chatOptions, cancellationToken);

            _logger.LogInformation(
                "Azure OpenAI chat completion succeeded (input tokens: {InputTokens}, output tokens: {OutputTokens})",
                completion.Usage?.InputTokenCount,
                completion.Usage?.OutputTokenCount);

            return completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex,
                "Azure OpenAI chat completion failed after retries (status: {Status})", ex.Status);
            throw new UpstreamServiceException("The summarization backend failed to process the request.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Azure OpenAI chat completion failed");
            throw new UpstreamServiceException("The summarization backend could not be reached.", ex);
        }
    }
}

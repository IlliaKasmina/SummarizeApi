using SummarizeApi.Models;

namespace SummarizeApi.Services;

/// <summary>
/// Business logic for text summarization: prompt construction, model
/// parameters, and response shaping. Input is assumed to be already validated
/// (non-empty text, length and range limits enforced at the HTTP layer).
/// </summary>
public interface ISummarizationService
{
    Task<SummarizeResponse> SummarizeAsync(
        string text,
        int maxWords,
        CancellationToken cancellationToken);
}

namespace SummarizeApi.Infrastructure;

/// <summary>
/// Raised when the upstream Azure OpenAI call fails after the SDK has
/// exhausted its retries. The global exception handler maps this to a 502
/// ProblemDetails response without exposing upstream internals.
/// </summary>
public sealed class UpstreamServiceException : Exception
{
    public UpstreamServiceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

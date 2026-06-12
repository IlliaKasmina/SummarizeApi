using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SummarizeApi.Infrastructure;

namespace SummarizeApi.Middleware;

/// <summary>
/// Terminal exception handler plugged into ASP.NET Core's exception-handling
/// middleware (<c>UseExceptionHandler</c>). Maps
/// <see cref="UpstreamServiceException"/> to 502 and everything else to 500,
/// always as RFC 7807 ProblemDetails. Exception details are logged but never
/// returned to the caller.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is UpstreamServiceException)
        {
            _logger.LogError(exception, "Upstream Azure OpenAI failure for {Path}", httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Upstream service error",
                Detail = "The summarization service is temporarily unavailable. Please retry later.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
            };
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "The server encountered an internal error. Please retry later.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}

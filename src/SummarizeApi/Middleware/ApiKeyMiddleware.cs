using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SummarizeApi.Options;

namespace SummarizeApi.Middleware;

/// <summary>
/// Rejects requests without a valid X-Api-Key header. The health probe,
/// Swagger UI, and the OpenAPI document stay anonymous so that platform
/// probes and developers can reach them.
/// Key comparison uses <see cref="CryptographicOperations.FixedTimeEquals"/>
/// to avoid timing side channels.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private static readonly string[] AnonymousPathPrefixes = ["/health", "/swagger", "/openapi"];

    private readonly RequestDelegate _next;
    private readonly byte[] _expectedKeyBytes;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IOptions<ApiKeyOptions> options,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _expectedKeyBytes = Encoding.UTF8.GetBytes(options.Value.Key);
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var providedKey)
            || !IsValidKey(providedKey.ToString()))
        {
            _logger.LogWarning(
                "Rejected request to {Path}: missing or invalid API key", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = $"A valid {ApiKeyOptions.HeaderName} header is required.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static bool IsAnonymousPath(PathString path) =>
        AnonymousPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private bool IsValidKey(string providedKey)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, _expectedKeyBytes);
    }
}

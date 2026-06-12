using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SummarizeApi.Middleware;
using SummarizeApi.Options;
using Xunit;

namespace SummarizeApi.Tests;

public sealed class ApiKeyMiddlewareTests
{
    private const string ValidKey = "test-api-key-0123456789";

    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next,
            Microsoft.Extensions.Options.Options.Create(new ApiKeyOptions { Key = ValidKey }),
            NullLogger<ApiKeyMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(string path, string? apiKey = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (apiKey is not null)
        {
            context.Request.Headers[ApiKeyOptions.HeaderName] = apiKey;
        }
        return context;
    }

    [Fact]
    public async Task ValidKey_CallsNextMiddleware()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/summarize", ValidKey);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task MissingKey_Returns401AndSkipsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/summarize");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    [InlineData("test-api-key-0123456788")] // one char off
    public async Task WrongKey_Returns401(string providedKey)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/summarize", providedKey);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task AnonymousPaths_BypassKeyCheck(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NonAnonymousPath_RequiresKeyEvenIfPrefixSimilar()
    {
        // "/healthcheck" is NOT "/health" segment-wise; must require a key.
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/healthcheck");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}

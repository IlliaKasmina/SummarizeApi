using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SummarizeApi.Infrastructure;
using SummarizeApi.Services;
using Xunit;

namespace SummarizeApi.Tests;

public sealed class SummarizationServiceTests
{
    private readonly IOpenAIClientWrapper _client = Substitute.For<IOpenAIClientWrapper>();
    private readonly SummarizationService _service;

    public SummarizationServiceTests()
    {
        _service = new SummarizationService(_client, NullLogger<SummarizationService>.Instance);
    }

    [Fact]
    public async Task SummarizeAsync_HappyPath_ReturnsSummaryWithLengths()
    {
        const string inputText = "The quick brown fox jumps over the lazy dog. It was a sunny day.";
        const string modelOutput = "  A fox jumped over a dog on a sunny day.  ";

        _client.CompleteChatAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(modelOutput));

        var response = await _service.SummarizeAsync(
            inputText, 100, CancellationToken.None);

        Assert.Equal("A fox jumped over a dog on a sunny day.", response.Summary);
        Assert.Equal(inputText.Length, response.OriginalLength);
        Assert.Equal(response.Summary.Length, response.SummaryLength);
    }

    [Fact]
    public async Task SummarizeAsync_PassesWordBudgetIntoSystemPrompt()
    {
        string? capturedSystemPrompt = null;
        _client.CompleteChatAsync(
                Arg.Do<string>(p => capturedSystemPrompt = p),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("summary"));

        await _service.SummarizeAsync("some text", 250, CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("at most 250 words", capturedSystemPrompt);
    }

    [Fact]
    public async Task SummarizeAsync_PassesDerivedTokenBudget()
    {
        int capturedMaxTokens = -1;
        _client.CompleteChatAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<int>(t => capturedMaxTokens = t),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("summary"));

        await _service.SummarizeAsync("some text", 100, CancellationToken.None);

        Assert.Equal(SummarizationService.DeriveMaxOutputTokens(100), capturedMaxTokens);
    }

    [Fact]
    public async Task SummarizeAsync_UpstreamFailure_PropagatesUpstreamServiceException()
    {
        _client.CompleteChatAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UpstreamServiceException("backend failed"));

        await Assert.ThrowsAsync<UpstreamServiceException>(() =>
            _service.SummarizeAsync("some text", 100, CancellationToken.None));
    }

    [Theory]
    [InlineData(10, 52)]
    [InlineData(100, 232)]
    [InlineData(500, 1032)]
    [InlineData(1000, 1500)] // clamped to ceiling
    public void DeriveMaxOutputTokens_ScalesWithWordBudget(int maxWords, int expectedTokens)
    {
        Assert.Equal(expectedTokens, SummarizationService.DeriveMaxOutputTokens(maxWords));
    }

    [Fact]
    public void BuildUserPrompt_WrapsTextInDocumentFence()
    {
        var prompt = SummarizationService.BuildUserPrompt("hello world");

        Assert.Contains("<document>", prompt);
        Assert.Contains("hello world", prompt);
        Assert.Contains("</document>", prompt);
    }
}

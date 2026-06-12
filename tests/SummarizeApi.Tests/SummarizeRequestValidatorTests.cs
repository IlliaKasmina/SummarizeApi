using SummarizeApi.Models;
using SummarizeApi.Validation;
using Xunit;

namespace SummarizeApi.Tests;

public sealed class SummarizeRequestValidatorTests
{
    private readonly SummarizeRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequestWithOnlyText_Passes()
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = "Some text to summarize." });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrEmptyText_FailsOnTextField(string? text)
    {
        var result = _validator.Validate(new SummarizeRequest { Text = text });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "text");
    }

    [Fact]
    public void Validate_TextOverFiftyThousandChars_FailsOnTextField()
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = new string('a', 50_001) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "text");
    }

    [Fact]
    public void Validate_TextAtExactlyFiftyThousandChars_Passes()
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = new string('a', 50_000) });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(501)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_MaxWordsOutOfRange_FailsOnMaxWordsField(int maxWords)
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = "valid text", MaxWords = maxWords });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "maxWords");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public void Validate_MaxWordsAtBoundaries_Passes(int maxWords)
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = "valid text", MaxWords = maxWords });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MultipleInvalidFields_ReportsAll()
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = "", MaxWords = 5 });

        Assert.False(result.IsValid);
        var fields = result.Errors.Select(e => e.PropertyName).Distinct().ToList();
        Assert.Equal(2, fields.Count);
        Assert.Contains("text", fields);
        Assert.Contains("maxWords", fields);
    }

    [Fact]
    public void Validate_ErrorKeysAreCamelCase_ForProblemDetailsContract()
    {
        var result = _validator.Validate(
            new SummarizeRequest { Text = "", MaxWords = 5 });

        var errors = result.ToDictionary();
        Assert.Contains("text", errors.Keys);
        Assert.Contains("maxWords", errors.Keys);
    }

    [Theory]
    [InlineData(null, 100)] // default applied
    [InlineData(250, 250)]
    public void NormalizeMaxWords_AppliesDefault(int? input, int expected)
    {
        Assert.Equal(expected, SummarizeRequestValidator.NormalizeMaxWords(input));
    }
}

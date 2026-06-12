using FluentValidation;
using SummarizeApi.Models;

namespace SummarizeApi.Validation;

/// <summary>
/// FluentValidation rules for <see cref="SummarizeRequest"/>. Property names
/// are overridden to camelCase so ProblemDetails error keys match the JSON
/// contract. Defaulting of the optional word budget lives in the static
/// helper below and is applied by the endpoint after validation.
/// </summary>
public sealed class SummarizeRequestValidator : AbstractValidator<SummarizeRequest>
{
    public const int MaxTextLength = 50_000;
    public const int MinWords = 10;
    public const int MaxWords = 500;
    public const int DefaultMaxWords = 100;

    public SummarizeRequestValidator()
    {
        RuleFor(x => x.Text)
            .Cascade(CascadeMode.Stop)
            .Must(text => !string.IsNullOrWhiteSpace(text))
                .WithMessage("Text is required and must not be empty.")
            .MaximumLength(MaxTextLength)
                .WithMessage(x =>
                    $"Text must not exceed {MaxTextLength:N0} characters (got {x.Text!.Length:N0}).")
            .OverridePropertyName("text");

        RuleFor(x => x.MaxWords)
            .InclusiveBetween(MinWords, MaxWords)
                .When(x => x.MaxWords.HasValue)
                .WithMessage($"maxWords must be between {MinWords} and {MaxWords}.")
            .OverridePropertyName("maxWords");
    }

    /// <summary>Applies the default word budget for omitted maxWords.</summary>
    public static int NormalizeMaxWords(int? maxWords) => maxWords ?? DefaultMaxWords;
}

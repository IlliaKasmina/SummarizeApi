namespace SummarizeApi.Models;

/// <summary>
/// Request body for POST /summarize.
/// Optional fields are nullable; defaults are applied after validation
/// (MaxWords = 100).
/// </summary>
public sealed record SummarizeRequest
{
    /// <summary>Text to summarize. Required, 1–50,000 characters.</summary>
    public string? Text { get; init; }

    /// <summary>Target summary length in words. Optional, 10–500, default 100.</summary>
    public int? MaxWords { get; init; }
}

namespace SummarizeApi.Models;

/// <summary>Response body for a successful POST /summarize.</summary>
public sealed record SummarizeResponse(
    string Summary,
    int OriginalLength,
    int SummaryLength);

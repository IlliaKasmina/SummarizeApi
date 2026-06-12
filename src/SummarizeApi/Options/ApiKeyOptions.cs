using System.ComponentModel.DataAnnotations;

namespace SummarizeApi.Options;

/// <summary>
/// API key expected in the X-Api-Key request header. Bound from the "ApiKey"
/// configuration section. Supplied via user-secrets locally and App Service
/// app settings in Azure — never committed to source.
/// </summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    public const string HeaderName = "X-Api-Key";

    [Required]
    [MinLength(16, ErrorMessage = "API key must be at least 16 characters.")]
    public string Key { get; set; } = string.Empty;
}

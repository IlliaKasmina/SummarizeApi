using System.ComponentModel.DataAnnotations;

namespace SummarizeApi.Options;

/// <summary>
/// Azure OpenAI connection settings. Bound from the "AzureOpenAI" configuration
/// section. No API key here on purpose: authentication uses
/// <c>DefaultAzureCredential</c> (managed identity in Azure, az login locally).
/// </summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Account endpoint, e.g. https://my-account.openai.azure.com/.</summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Model deployment name, e.g. "gpt-4o-mini".</summary>
    [Required]
    public string DeploymentName { get; set; } = string.Empty;
}

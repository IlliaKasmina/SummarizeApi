using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Azure.Identity;
using FluentValidation;
using Microsoft.Extensions.Options;
using SummarizeApi.Endpoints;
using SummarizeApi.Infrastructure;
using SummarizeApi.Middleware;
using SummarizeApi.OpenApi;
using SummarizeApi.Options;
using SummarizeApi.Services;
using SummarizeApi.Validation;

var builder = WebApplication.CreateBuilder(args);

// Telemetry: picks up APPLICATIONINSIGHTS_CONNECTION_STRING from app settings.
// Registered only when configured — the exporter throws at startup otherwise,
// which would break local runs without an App Insights resource.
var appInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// Options pattern with startup validation: fail fast on missing config.
builder.Services.AddOptions<AzureOpenAIOptions>()
    .BindConfiguration(AzureOpenAIOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Azure OpenAI client: DefaultAzureCredential (managed identity in Azure,
// az login locally). Retries on 408/429/5xx with exponential backoff are
// handled by the SDK pipeline; 4 retries before the failure surfaces as 502.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
    var clientOptions = new AzureOpenAIClientOptions
    {
        RetryPolicy = new ClientRetryPolicy(maxRetries: 4),
    };
    return new AzureOpenAIClient(new Uri(options.Endpoint), new DefaultAzureCredential(), clientOptions);
});

builder.Services.AddSingleton<IOpenAIClientWrapper, OpenAIClientWrapper>();
builder.Services.AddSingleton<ISummarizationService, SummarizationService>();
builder.Services.AddValidatorsFromAssemblyContaining<SummarizeRequestValidator>(
    lifetime: ServiceLifetime.Singleton);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddSwaggerWithApiKey();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapSummarizeEndpoints();

app.Run();

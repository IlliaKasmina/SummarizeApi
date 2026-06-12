using Microsoft.OpenApi;

namespace SummarizeApi.OpenApi;

/// <summary>
/// Swagger/OpenAPI registration, including the X-Api-Key header documented
/// as an apiKey security scheme so the "Authorize" button works in the UI.
/// </summary>
public static class SwaggerConfiguration
{
    public static IServiceCollection AddSwaggerWithApiKey(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Summarize API",
                Version = "v1",
                Description = "Summarizes text using Azure OpenAI.",
            });

            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "API key required for all endpoints except /health.",
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKey", document)] = [],
            });
        });

        return services;
    }
}

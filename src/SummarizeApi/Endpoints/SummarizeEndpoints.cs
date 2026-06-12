using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SummarizeApi.Models;
using SummarizeApi.Services;
using SummarizeApi.Validation;

namespace SummarizeApi.Endpoints;

/// <summary>
/// Thin HTTP layer: model binding, validation, and status-code mapping only.
/// All business logic lives in <see cref="ISummarizationService"/>.
/// </summary>
public static class SummarizeEndpoints
{
    public static IEndpointRouteBuilder MapSummarizeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/summarize", SummarizeAsync)
            .WithName("Summarize")
            .WithTags("Summarize")
            .WithSummary("Summarizes the supplied text using Azure OpenAI.")
            .Produces<SummarizeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithName("Health")
            .WithTags("Health")
            .WithSummary("Liveness probe; anonymous.")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> SummarizeAsync(
        [FromBody] SummarizeRequest request,
        IValidator<SummarizeRequest> validator,
        ISummarizationService summarizationService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SummarizeApi.Endpoints.Summarize");
        logger.LogInformation("POST /summarize received");

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.ToDictionary();
            logger.LogInformation("POST /summarize rejected with validation errors: {Fields}",
                string.Join(", ", errors.Keys));
            return Results.ValidationProblem(errors);
        }

        var response = await summarizationService.SummarizeAsync(
            request.Text!,
            SummarizeRequestValidator.NormalizeMaxWords(request.MaxWords),
            cancellationToken);

        return Results.Ok(response);
    }
}

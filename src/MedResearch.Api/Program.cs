using MedResearch.Api.Research;
using MedResearch.Application.DependencyInjection;
using MedResearch.Application.Research;
using MedResearch.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandler = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionHandler?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MedResearch.Api.ErrorHandling");

        var (statusCode, title) = exception switch
        {
            ArgumentException or InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing HTTP request.");
        }
        else
        {
            logger.LogInformation(exception, "Client request failed validation.");
        }

        context.Response.StatusCode = statusCode;
        await Results.Problem(
            title: title,
            statusCode: statusCode,
            extensions: statusCode == StatusCodes.Status400BadRequest
                ? new Dictionary<string, object?> { ["error"] = exception?.Message }
                : null)
            .ExecuteAsync(context);
    });
});

app.MapHealthChecks("/health");

var research = app.MapGroup("/api/research")
    .WithTags("Research");

research.MapPost("/", async (
        CreateResearchRequest request,
        CreateResearchUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(new CreateResearchCommand(request.Question), cancellationToken);
        var response = new CreateResearchResponse(result.ResearchRunId, result.Status);

        return Results.Created($"/api/research/{result.ResearchRunId}", response);
    })
    .WithName("CreateResearch")
    .Accepts<CreateResearchRequest>("application/json")
    .Produces<CreateResearchResponse>(StatusCodes.Status201Created)
    .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

research.MapGet("/{researchRunId:guid}", async (
        Guid researchRunId,
        GetResearchUseCase useCase,
        CancellationToken cancellationToken) =>
    {
        var result = await useCase.ExecuteAsync(researchRunId, cancellationToken);

        if (result is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Research run not found"
            });
        }

        return Results.Ok(new ResearchRunResponse(
            result.ResearchRunId,
            result.Question,
            result.Status,
            result.CreatedAt,
            result.StartedAt,
            result.CompletedAt,
            result.FailureReason));
    })
    .WithName("GetResearch")
    .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

app.Run();

public partial class Program
{
}

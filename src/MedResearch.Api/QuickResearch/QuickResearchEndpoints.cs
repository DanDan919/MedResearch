using System.Text.Json.Serialization;
using MedResearch.Application.QuickResearch;
using MedResearch.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace MedResearch.Api.QuickResearch;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateQuickResearchRequest(string Question);

public static class QuickResearchEndpoints
{
    public static void MapQuickResearch(this WebApplication app)
    {
        var quick = app.MapGroup("/api/quick-research").WithTags("Quick Research")
            .RequireAuthorization(AuthenticationConfiguration.PolicyName)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "private, no-store";
                return await next(context);
            });
        quick.MapPost("/", async (CreateQuickResearchRequest request, QuickResearchService service, CancellationToken token) =>
        {
            var result = await service.SubmitAsync(request.Question, token);
            return Results.Accepted($"/api/quick-research/{result.Id}", result);
        }).WithName("CreateQuickResearch").Produces<QuickResearchDetails>(StatusCodes.Status202Accepted)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);
        quick.MapGet("/{id:guid}", async (Guid id, QuickResearchService service, CancellationToken token) =>
            await service.FindAsync(id, token) is { } result ? Results.Ok(result) : Results.NotFound())
            .WithName("GetQuickResearch").Produces<QuickResearchDetails>().Produces(StatusCodes.Status404NotFound);
        quick.MapGet("/", async (int? page, int? pageSize, QuickResearchService service, CancellationToken token) =>
            Results.Ok(await service.HistoryAsync(page ?? 1, pageSize ?? 20, token)))
            .WithName("ListQuickResearch").Produces<QuickResearchHistory>().Produces<ProblemDetails>(StatusCodes.Status400BadRequest);
    }
}

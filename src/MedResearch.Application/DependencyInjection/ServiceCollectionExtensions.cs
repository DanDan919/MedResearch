using MedResearch.Application.Research;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace MedResearch.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateResearchUseCase>();
        services.AddScoped<GetResearchUseCase>();
        services.AddScoped<ResearchRunProcessor>();
        services.AddScoped<IResearchStageExecutor, ScientificResearchStageExecutor>();
        services.AddSingleton<IScientificSearchQueryBuilder, DeterministicScientificSearchQueryBuilder>();

        return services;
    }
}

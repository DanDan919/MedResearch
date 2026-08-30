using MedResearch.Application.Research;
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
        services.AddSingleton<IResearchStageExecutor, DeterministicResearchStageExecutor>();

        return services;
    }
}

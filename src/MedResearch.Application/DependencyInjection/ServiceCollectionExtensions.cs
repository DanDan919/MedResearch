using Microsoft.Extensions.DependencyInjection;
using MedResearch.Application.Research;

namespace MedResearch.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateResearchUseCase>();
        services.AddScoped<GetResearchUseCase>();

        return services;
    }
}

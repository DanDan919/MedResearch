using MedResearch.Application.Research;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace MedResearch.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateResearchUseCase>();
        services.AddScoped<GetResearchUseCase>();
        services.AddScoped<IResearchPlanner, ResearchPlanner>();
        services.AddScoped<EvidenceGroundingValidator>();
        services.AddScoped<EvidenceNumericGroundingValidator>();
        services.AddScoped<EvidenceExtractionDraftValidator>();
        services.AddScoped<IEvidenceExtractor, EvidenceExtractor>();
        services.AddScoped<ResearchRunProcessor>();
        services.AddScoped<IResearchStageExecutor, ScientificResearchStageExecutor>();

        return services;
    }
}

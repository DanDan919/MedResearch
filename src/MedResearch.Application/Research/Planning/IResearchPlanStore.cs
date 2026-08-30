using MedResearch.Domain;

namespace MedResearch.Application.Research.Planning;

public interface IResearchPlanStore
{
    Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken);

    Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken);
}

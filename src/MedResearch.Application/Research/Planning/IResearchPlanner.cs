using MedResearch.Domain;

namespace MedResearch.Application.Research.Planning;

public interface IResearchPlanner
{
    Task<ResearchPlan> GenerateAndPersistPlanAsync(
        Guid researchRunId,
        Guid researchQuestionId,
        string researchQuestion,
        CancellationToken cancellationToken);
}

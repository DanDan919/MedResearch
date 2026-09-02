using MedResearch.Domain;

namespace MedResearch.Application.Research.Literature;

public interface IScientificLiteratureSearchCoordinator
{
    Task SearchAsync(
        Guid researchRunId,
        Guid researchPlanId,
        IReadOnlyCollection<string> queries,
        CancellationToken cancellationToken);
}

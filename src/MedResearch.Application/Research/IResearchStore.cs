using MedResearch.Domain;

namespace MedResearch.Application.Research;

public interface IResearchStore
{
    Task PersistInitialResearchAsync(ResearchQuestion question, ResearchRun run, CancellationToken cancellationToken);

    Task<ResearchRunDetails?> FindResearchRunAsync(Guid researchRunId, CancellationToken cancellationToken);
}

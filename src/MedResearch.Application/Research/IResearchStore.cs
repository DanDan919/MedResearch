using MedResearch.Domain;

namespace MedResearch.Application.Research;

public interface IResearchStore
{
    Task PersistInitialResearchAsync(
        ResearchQuestion question,
        ResearchRun run,
        string ownerSubjectId,
        CancellationToken cancellationToken);

    Task<ResearchRunDetails?> FindResearchRunAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken);

    Task<ResearchRunListResult> ListResearchRunsAsync(
        int page,
        int pageSize,
        ResearchRunStatus? status,
        string ownerSubjectId,
        CancellationToken cancellationToken);
}

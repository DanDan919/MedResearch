using MedResearch.Domain;

namespace MedResearch.Application.Research;

public interface IResearchStore
{
    Task<CreateResearchResult> PersistInitialResearchAsync(
        ResearchQuestion question,
        ResearchRun run,
        string ownerSubjectId,
        Guid idempotencyKey,
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

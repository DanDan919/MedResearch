namespace MedResearch.Application.Research;

public interface IResearchProgressStore
{
    Task<ResearchRunProgressSnapshot?> FindResearchRunProgressSnapshotAsync(
        Guid researchRunId,
        CancellationToken cancellationToken);
}

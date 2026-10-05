namespace MedResearch.Application.Research.Provenance;

public interface IResearchProvenanceStore
{
    Task<ResearchProvenanceReadModel?> FindAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken);
}

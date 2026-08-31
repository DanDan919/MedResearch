namespace MedResearch.Application.Research.Extraction;

public interface IEvidenceExtractionStore
{
    Task<EvidenceExtractionWorkItemSet> FindStudiesForExtractionAsync(
        Guid researchRunId,
        string promptVersion,
        int maxStudies,
        CancellationToken cancellationToken);

    Task PersistExtractionResultAsync(
        EvidenceExtractionResult result,
        CancellationToken cancellationToken);
}

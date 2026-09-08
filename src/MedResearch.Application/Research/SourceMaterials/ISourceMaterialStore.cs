namespace MedResearch.Application.Research.SourceMaterials;

public interface ISourceMaterialStore
{
    Task<SourceMaterialAcquisitionStudySet> FindStudiesForSourceAcquisitionAsync(
        Guid researchRunId,
        int maxStudies,
        CancellationToken cancellationToken);

    Task<SourceMaterialPersistenceResult> PersistSourceMaterialAsync(
        Guid studyId,
        SourceMaterialCandidate candidate,
        CancellationToken cancellationToken);
}

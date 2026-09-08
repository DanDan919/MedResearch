namespace MedResearch.Application.Research.SourceMaterials;

public interface ISourceMaterialAcquirer
{
    Task<SourceMaterialAcquisitionResult> AcquireForResearchRunAsync(
        Guid researchRunId,
        CancellationToken cancellationToken);
}

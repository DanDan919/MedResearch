namespace MedResearch.Application.Research.SourceMaterials;

public interface ISourceMaterialProvider
{
    string ProviderName { get; }

    Task<SourceMaterialCandidate?> TryAcquireAsync(
        SourceMaterialStudyContext study,
        int maxContentCharacters,
        CancellationToken cancellationToken);
}

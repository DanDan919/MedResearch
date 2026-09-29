namespace MedResearch.Application.Research.Quantitative;

public sealed class GetQuantitativeSynthesisArtifactsUseCase
{
    private readonly IQuantitativeSynthesisArtifactStore _store;

    public GetQuantitativeSynthesisArtifactsUseCase(IQuantitativeSynthesisArtifactStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> ExecuteAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        return _store.FindByResearchRunIdAsync(researchRunId, cancellationToken);
    }
}

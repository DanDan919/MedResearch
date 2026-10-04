namespace MedResearch.Application.Research.Quantitative;

using MedResearch.Application.Security;

public sealed class GetQuantitativeSynthesisArtifactsUseCase
{
    private readonly IQuantitativeSynthesisArtifactStore _store;
    private readonly ICurrentActor _currentActor;

    public GetQuantitativeSynthesisArtifactsUseCase(
        IQuantitativeSynthesisArtifactStore store,
        ICurrentActor currentActor)
    {
        _store = store;
        _currentActor = currentActor;
    }

    public Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> ExecuteAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        return _store.FindByResearchRunIdAsync(
            researchRunId,
            _currentActor.RequireSubjectId(),
            cancellationToken);
    }
}

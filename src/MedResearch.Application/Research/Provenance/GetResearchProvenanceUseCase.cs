using MedResearch.Application.Security;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Provenance;

public sealed class GetResearchProvenanceUseCase
{
    private readonly IResearchProvenanceStore _store;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<GetResearchProvenanceUseCase> _logger;

    public GetResearchProvenanceUseCase(
        IResearchProvenanceStore store,
        ICurrentActor currentActor,
        ILogger<GetResearchProvenanceUseCase> logger)
    {
        _store = store;
        _currentActor = currentActor;
        _logger = logger;
    }

    public async Task<ResearchProvenanceReadModel?> ExecuteAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var result = await _store.FindAsync(
            researchRunId,
            _currentActor.RequireSubjectId(),
            cancellationToken);

        if (result is null)
        {
            _logger.LogInformation("Research provenance not found. ResearchRunId: {ResearchRunId}", researchRunId);
            return null;
        }

        _logger.LogInformation(
            "Research provenance retrieved. ResearchRunId: {ResearchRunId}; StudyCount: {StudyCount}; EvidenceCount: {EvidenceCount}",
            result.ResearchRunId,
            result.Coverage.DistinctStudyCount,
            result.Coverage.EvidenceFindingCount);

        return result;
    }
}

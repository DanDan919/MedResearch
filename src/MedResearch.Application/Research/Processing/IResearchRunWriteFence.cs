namespace MedResearch.Application.Research.Processing;

/// <summary>
/// Prevents a worker that lost its lease from committing stage output.
/// </summary>
public interface IResearchRunWriteFence
{
    void Attach(ClaimedResearchRun claimedRun);

    void Clear();

    Task AssertOwnedAsync(Guid researchRunId, CancellationToken cancellationToken);

    Task AssertStudyBelongsToRunAsync(Guid studyId, CancellationToken cancellationToken);
}

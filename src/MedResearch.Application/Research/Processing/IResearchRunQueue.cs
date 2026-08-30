namespace MedResearch.Application.Research.Processing;

public interface IResearchRunQueue
{
    Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(DateTimeOffset claimedAt, CancellationToken cancellationToken);

    Task SaveProgressAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        Guid researchRunId,
        string safeFailureReason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

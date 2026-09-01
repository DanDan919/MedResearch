namespace MedResearch.Application.Research.Processing;

public interface IResearchRunQueue
{
    Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(
        DateTimeOffset claimedAt,
        string workerInstanceId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        ClaimedResearchRun claimedRun,
        DateTimeOffset heartbeatAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> SaveProgressAsync(
        ClaimedResearchRun claimedRun,
        DateTimeOffset savedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        ClaimedResearchRun claimedRun,
        string safeFailureReason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);

    Task<bool> ReleaseLeaseAsync(
        ClaimedResearchRun claimedRun,
        CancellationToken cancellationToken);
}
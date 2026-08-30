using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public interface IResearchRunQueue
{
    Task<ResearchRun?> TryClaimNextQueuedRunAsync(DateTimeOffset claimedAt, CancellationToken cancellationToken);

    Task SaveProgressAsync(ResearchRun run, CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        Guid researchRunId,
        string safeFailureReason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

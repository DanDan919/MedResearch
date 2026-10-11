using MedResearch.Domain.QuickResearch;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.QuickResearch;

public sealed class QuickResearchProcessor(IQuickResearchStore store, ILogger<QuickResearchProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken token)
    {
        var claim = await store.ClaimAsync(workerId, leaseDuration, token);
        if (claim is null) return false;
        logger.LogInformation("QuickResearchClaimed. QuickRunId: {QuickRunId}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}", claim.RunId, workerId, claim.LeaseVersion);
        try
        {
            token.ThrowIfCancellationRequested();
            // Q0 has no provider dependency: this is a synthetic completion, not medical research.
            var saved = await store.CompleteAsync(claim, QuickResearchResult.CreateSynthetic(claim.RunId), token);
            logger.LogInformation("QuickResearchSettled. QuickRunId: {QuickRunId}; Saved: {Saved}; LeaseVersion: {LeaseVersion}", claim.RunId, saved, claim.LeaseVersion);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogWarning("QuickResearchFailed. QuickRunId: {QuickRunId}; LeaseVersion: {LeaseVersion}", claim.RunId, claim.LeaseVersion);
            await store.FailAsync(claim, token);
        }
        return true;
    }
}

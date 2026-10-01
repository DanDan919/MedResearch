using MedResearch.Application.Research.Processing;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Research.Processing;

/// <summary>
/// A transaction-bound PostgreSQL fence for stage persistence.
/// The row lock makes the owner/version check atomic with the stage write.
/// </summary>
public sealed class PostgreSqlResearchRunWriteFence : IResearchRunWriteFence
{
    private readonly MedResearchDbContext _dbContext;
    private ClaimedResearchRun? _claimedRun;

    public PostgreSqlResearchRunWriteFence(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Attach(ClaimedResearchRun claimedRun)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        _claimedRun = claimedRun;
    }

    public void Clear()
    {
        _claimedRun = null;
    }

    public async Task AssertOwnedAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var claimedRun = _claimedRun;
        if (claimedRun is null)
        {
            return;
        }

        if (claimedRun.Run.Id != researchRunId)
        {
            throw new ResearchRunLeaseLostException(
                claimedRun.Run.Id,
                claimedRun.WorkerInstanceId,
                claimedRun.LeaseVersion);
        }

        var ownedRun = await _dbContext.ResearchRuns
            .FromSqlInterpolated($"""
                SELECT *
                FROM research_runs
                WHERE id = {claimedRun.Run.Id}
                  AND processing_lease_owner = {claimedRun.WorkerInstanceId}
                  AND processing_lease_version = {claimedRun.LeaseVersion}
                  AND processing_lease_expires_at > CURRENT_TIMESTAMP
                  AND status IN ('Planning', 'Searching', 'Extracting', 'Evaluating', 'Synthesizing')
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (ownedRun is null)
        {
            throw new ResearchRunLeaseLostException(
                claimedRun.Run.Id,
                claimedRun.WorkerInstanceId,
                claimedRun.LeaseVersion);
        }
    }

    public async Task AssertStudyBelongsToRunAsync(Guid studyId, CancellationToken cancellationToken)
    {
        var claimedRun = _claimedRun;
        if (claimedRun is null)
        {
            return;
        }

        await AssertOwnedAsync(claimedRun.Run.Id, cancellationToken);

        var discovered = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .AnyAsync(
                discovery => discovery.ResearchRunId == claimedRun.Run.Id && discovery.StudyId == studyId,
                cancellationToken);
        if (!discovered)
        {
            throw new InvalidOperationException("A worker may only persist source material for a Study discovered by its ResearchRun.");
        }
    }
}

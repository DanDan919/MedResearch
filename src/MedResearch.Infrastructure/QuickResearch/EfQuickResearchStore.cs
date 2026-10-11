using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.QuickResearch;

public sealed class EfQuickResearchStore(MedResearchDbContext db) : IQuickResearchStore
{
    public async Task CreateAsync(QuickResearchRun run, CancellationToken token)
    {
        db.QuickResearchRuns.Add(run);
        await db.SaveChangesAsync(token);
    }

    public async Task<QuickResearchDetails?> FindAsync(Guid id, string owner, CancellationToken token)
    {
        var run = await db.QuickResearchRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerSubjectId == owner, token);
        if (run is null) return null;
        run.Result?.ValidateQ0(run.Id);
        return new(run.Id, run.Question, run.Status.ToString(), run.CreatedAt, run.StartedAt, run.CompletedAt, run.SafeError, run.Result);
    }

    public async Task<QuickResearchHistory> ListAsync(string owner, int page, int pageSize, CancellationToken token)
    {
        var owned = db.QuickResearchRuns.AsNoTracking().Where(x => x.OwnerSubjectId == owner);
        var count = await owned.CountAsync(token);
        var runs = await owned.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new QuickResearchSummary(x.Id, x.Question, x.Status.ToString(), x.CreatedAt, x.CompletedAt)).ToArrayAsync(token);
        return new(runs, page, pageSize, count);
    }

    public async Task<QuickResearchClaim?> ClaimAsync(string workerId, TimeSpan leaseDuration, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 200 || leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid Quick worker lease.");
        // One statement owns both candidate locking and lease transfer; no Deep queue is touched.
        var claimed = await db.QuickResearchRuns.FromSqlInterpolated($"""
            WITH candidate AS (
                SELECT id FROM quick_research_runs
                WHERE status = 'Queued' OR (status = 'Processing' AND lease_expires_at <= clock_timestamp())
                ORDER BY created_at, id FOR UPDATE SKIP LOCKED LIMIT 1
            ) UPDATE quick_research_runs run
            SET status = 'Processing', started_at = COALESCE(started_at, clock_timestamp()),
                lease_owner = {workerId}, lease_expires_at = clock_timestamp() + {leaseDuration}, lease_version = lease_version + 1
            FROM candidate WHERE run.id = candidate.id RETURNING run.*
            """).AsNoTracking().ToArrayAsync(token);
        var run = claimed.SingleOrDefault();
        return run is null ? null : new(run.Id, workerId, run.LeaseVersion);
    }

    public async Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token)
    {
        result.ValidateQ0(claim.RunId);
        var payload = JsonSerializer.Serialize(result);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET status = 'Completed', result = CAST({payload} AS jsonb), completed_at = clock_timestamp(),
                lease_owner = NULL, lease_expires_at = NULL
            WHERE id = {claim.RunId} AND status = 'Processing' AND lease_owner = {claim.WorkerId}
                AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
            """, token) == 1;
    }

    public async Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET status = 'Failed', safe_error = 'QuickProcessingFailed', completed_at = clock_timestamp(),
                lease_owner = NULL, lease_expires_at = NULL
            WHERE id = {claim.RunId} AND status = 'Processing' AND lease_owner = {claim.WorkerId}
                AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
            """, token) == 1;
}

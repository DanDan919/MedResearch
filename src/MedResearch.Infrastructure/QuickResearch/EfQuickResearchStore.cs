using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.QuickResearch;

public sealed class EfQuickResearchStore(MedResearchDbContext db) : IQuickResearchStore, IQuickAnswerStore
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
        ValidateStored(run);
        return new(run.Id, run.Question, run.Status.ToString(), run.CreatedAt, run.StartedAt, run.CompletedAt, run.SafeError, run.Result,
            run.Retrieval is null ? null : QuickRetrievalResult.From(run.Retrieval), run.AnswerGeneration?.Answer, run.AnswerGeneration?.Outcome.ToString());
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
        if (run is not null) ValidateStored(run);
        return run is null ? null : new(run.Id, workerId, run.LeaseVersion, run.Question, run.Retrieval, run.AnswerGeneration);
    }

    public async Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token)
    {
        result.ValidateQ0(claim.RunId);
        var payload = JsonSerializer.Serialize(result);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET status = 'Completed', result = CAST({payload} AS jsonb), completed_at = clock_timestamp(),
                lease_owner = NULL, lease_expires_at = NULL
            WHERE id = {claim.RunId} AND retrieval IS NULL AND answer_generation IS NULL AND status = 'Processing' AND lease_owner = {claim.WorkerId}
                AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
            """, token) == 1;
    }

    public async Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET status = 'Failed', safe_error = 'QuickProcessingFailed', completed_at = clock_timestamp(),
                lease_owner = NULL, lease_expires_at = NULL
            WHERE id = {claim.RunId} AND answer_generation IS NULL AND status = 'Processing' AND lease_owner = {claim.WorkerId}
                AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
            """, token) == 1;

    public async Task<bool> SaveRetrievalAsync(QuickResearchClaim claim, QuickRetrievalSnapshot snapshot, bool settle, CancellationToken token)
    {
        snapshot.Validate(claim.RunId);
        if (settle && !snapshot.IsFinal) throw new ArgumentException("Quick retrieval settlement requires a final snapshot.");
        var payload = JsonSerializer.Serialize(snapshot);
        var status = !settle ? "Processing" : snapshot.Outcome == QuickRetrievalOutcome.Failed ? "Failed" : "Completed";
        string? error = status == "Failed" ? "QuickRetrievalFailed" : null;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET retrieval = CAST({payload} AS jsonb), status = {status}, safe_error = {error},
                completed_at = CASE WHEN {settle} THEN clock_timestamp() ELSE NULL END,
                lease_owner = CASE WHEN {settle} THEN NULL ELSE lease_owner END,
                lease_expires_at = CASE WHEN {settle} THEN NULL ELSE lease_expires_at END
            WHERE id = {claim.RunId} AND status = 'Processing' AND result IS NULL AND answer_generation IS NULL
                AND (retrieval IS NULL OR retrieval ->> 'IsFinal' <> 'true') AND lease_owner = {claim.WorkerId}
                AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
            """, token) == 1;
    }

    public async Task<bool> ReserveAnswerAsync(QuickResearchClaim claim, QuickAnswerGeneration generation, CancellationToken token)
    {
        ValidateGeneration(claim, generation);
        if (generation.Outcome != QuickAnswerOutcome.Started) throw new ArgumentException("Quick reservation must be pending.");
        var payload = JsonSerializer.Serialize(generation);
        var retrieval = JsonSerializer.Serialize(claim.Retrieval);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET answer_generation = CAST({payload} AS jsonb)
            WHERE id = {claim.RunId} AND status = 'Processing' AND result IS NULL AND answer_generation IS NULL
                AND question = {claim.Question} AND retrieval = CAST({retrieval} AS jsonb)
                AND lease_owner = {claim.WorkerId} AND lease_version = {claim.LeaseVersion}
                AND lease_expires_at > clock_timestamp()
            """, token) == 1;
    }

    public async Task<bool> SettleAnswerAsync(QuickResearchClaim claim, QuickAnswerGeneration generation, CancellationToken token)
    {
        ValidateGeneration(claim, generation);
        if (generation.Outcome == QuickAnswerOutcome.Started) throw new ArgumentException("Quick settlement must be final.");
        var payload = JsonSerializer.Serialize(generation);
        var retrieval = JsonSerializer.Serialize(claim.Retrieval);
        var status = generation.Answer is null ? "Failed" : "Completed";
        string? error = status == "Completed" ? null : generation.FailureCategory == "RetrievalFailed" ? "QuickRetrievalFailed"
            : generation.Outcome == QuickAnswerOutcome.OutcomeUnknown ? "QuickAnswerOutcomeUnknown" : "QuickAnswerFailed";
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quick_research_runs SET answer_generation = CAST({payload} AS jsonb), status = {status},
                safe_error = {error}, completed_at = clock_timestamp(), lease_owner = NULL, lease_expires_at = NULL
            WHERE id = {claim.RunId} AND status = 'Processing' AND result IS NULL
                AND question = {claim.Question} AND retrieval = CAST({retrieval} AS jsonb)
                AND lease_owner = {claim.WorkerId} AND lease_version = {claim.LeaseVersion} AND lease_expires_at > clock_timestamp()
                AND (({generation.AttemptCount} = 0 AND answer_generation IS NULL)
                    OR ({generation.AttemptCount} = 1 AND answer_generation ->> 'Id' = {generation.Id.ToString()}
                        AND answer_generation ->> 'Outcome' = '0' AND answer_generation ->> 'AttemptCount' = '1'
                        AND answer_generation ->> 'InputFingerprint' = {generation.InputFingerprint}))
            """, token) == 1;
    }

    private static void ValidateGeneration(QuickResearchClaim claim, QuickAnswerGeneration generation)
    {
        generation.Validate(claim.RunId);
        if (claim.Retrieval is null || !claim.Retrieval.IsFinal) throw new ArgumentException("Quick answer requires saved final retrieval.");
        claim.Retrieval.Validate(claim.RunId);
        if (generation.Answer is not null) QuickAnswerValidator.ValidateStored(generation.Answer, claim.Retrieval);
    }

    private static void ValidateStored(QuickResearchRun run)
    {
        try
        {
            run.Result?.ValidateQ0(run.Id);
            run.Retrieval?.Validate(run.Id);
            run.AnswerGeneration?.Validate(run.Id);
            if (run.AnswerGeneration is not null && run.Retrieval?.IsFinal != true)
                throw new ArgumentException("Missing Quick answer input.");
            if (run.AnswerGeneration?.Answer is { } answer) QuickAnswerValidator.ValidateStored(answer, run.Retrieval!);
        }
        catch (ArgumentException) { throw new InvalidDataException("Stored Quick data failed integrity validation."); }
    }
}

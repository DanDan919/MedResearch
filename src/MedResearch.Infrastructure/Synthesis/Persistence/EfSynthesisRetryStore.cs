using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Processing;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class EfSynthesisRetryStore(
    MedResearchDbContext db,
    SynthesisRetryInputs inputs,
    ValidationGuidedLlmRepairOptions repair,
    IResearchRunWriteFence fence,
    ILogger<EfSynthesisRetryStore>? logger = null) : ISynthesisRetryStore, ISynthesisRetryGenerationBudget
{
    public async Task<SynthesisRetryState?> GetAsync(Guid runId, string owner, CancellationToken token)
    {
        var run = await OwnedRun(runId, owner).SingleOrDefaultAsync(token);
        if (run is null) return null;
        var existing = await db.SynthesisRetryAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.ResearchRunId == runId, token);
        if (existing is not null) return Project(existing);
        var eligible = await IsEligibleAsync(run, owner, token);
        return new(runId, eligible ? "Available" : "Unavailable", null, null, 1 + repair.MaxSemanticRepairAttempts, 0, null);
    }

    public async Task<SynthesisRetryState?> AdmitAsync(Guid runId, string owner, Guid key, CancellationToken token)
    {
        if (key == Guid.Empty) throw new ArgumentException("A valid retry idempotency key is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Lock the ownership root's run before checking the unique per-run retry budget.
        var run = await db.ResearchRuns.FromSqlInterpolated($"""
            SELECT run.* FROM research_runs run JOIN research_questions question ON question.id = run.research_question_id
            WHERE run.id = {runId} AND question.owner_subject_id = {owner} FOR UPDATE OF run
            """).AsNoTracking().SingleOrDefaultAsync(token);
        if (run is null) return null;
        var hash = SynthesisRetryInputs.Hash($"synthesis-retry-v1\n{owner}\n{runId:D}\n{key:D}");
        var existing = await db.SynthesisRetryAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.ResearchRunId == runId, token);
        if (existing is not null)
        {
            if (existing.RequestedByOwner != owner || existing.IdempotencyKeyHash != hash) throw new SynthesisRetryConflictException();
            await transaction.CommitAsync(token);
            return Project(existing);
        }
        if (!await IsEligibleAsync(run, owner, token)) throw new SynthesisRetryConflictException();
        var fingerprint = await inputs.ValidateAndFingerprintAsync(runId, owner, token);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
        var attempt = new SynthesisRetryAttemptEntity
        {
            Id = Guid.NewGuid(), ResearchRunId = runId, RequestedByOwner = owner, IdempotencyKeyHash = hash,
            InputFingerprint = fingerprint, RequestedAt = now, GenerationBudget = 1 + repair.MaxSemanticRepairAttempts
        };
        db.SynthesisRetryAttempts.Add(attempt);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger?.LogInformation("SynthesisRetryAdmitted. ResearchRunId: {ResearchRunId}; RetryAttemptId: {RetryAttemptId}; GenerationBudget: {GenerationBudget}", runId, attempt.Id, attempt.GenerationBudget);
        return Project(attempt);
    }

    public async Task ValidateInputsAsync(Guid runId, CancellationToken token)
    {
        var claim = fence.CurrentClaim;
        if (claim?.SynthesisRetryId is null) return;
        if (claim.Run.Id != runId) throw new ResearchRunLeaseLostException(runId, claim.WorkerInstanceId, claim.LeaseVersion);
        var attempt = await db.SynthesisRetryAttempts.AsNoTracking().SingleAsync(x => x.Id == claim.SynthesisRetryId, token);
        if (attempt.GenerationPending) throw new SynthesisRetryBudgetException(true);
        if (attempt.Status != "Running" || attempt.LeaseVersion != claim.LeaseVersion
            || attempt.InputFingerprint != await inputs.ValidateAndFingerprintAsync(runId, attempt.RequestedByOwner, token))
            throw new SynthesisRetryConflictException();
    }

    public async Task ReserveAsync(Guid runId, CancellationToken token)
    {
        var claim = fence.CurrentClaim;
        if (claim?.SynthesisRetryId is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await fence.AssertOwnedAsync(runId, token);
        await ValidateInputsAsync(runId, token);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE synthesis_retry_attempts SET generation_calls_reserved = generation_calls_reserved + 1, generation_pending = TRUE
            WHERE id = {claim.SynthesisRetryId.Value} AND research_run_id = {runId} AND status = 'Running'
              AND lease_version = {claim.LeaseVersion} AND NOT generation_pending AND generation_calls_reserved < generation_budget
            """, token);
        if (affected != 1) throw new SynthesisRetryBudgetException(false);
        await transaction.CommitAsync(token);
        logger?.LogInformation("SynthesisRetryGenerationReserved. ResearchRunId: {ResearchRunId}; RetryAttemptId: {RetryAttemptId}; LeaseVersion: {LeaseVersion}", runId, claim.SynthesisRetryId, claim.LeaseVersion);
    }

    public async Task RecordResponseAsync(Guid runId, CancellationToken token)
    {
        var claim = fence.CurrentClaim;
        if (claim?.SynthesisRetryId is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await fence.AssertOwnedAsync(runId, token);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE synthesis_retry_attempts SET generation_pending = FALSE
            WHERE id = {claim.SynthesisRetryId.Value} AND research_run_id = {runId} AND status = 'Running'
              AND lease_version = {claim.LeaseVersion} AND generation_pending
            """, token);
        if (affected != 1) throw new ResearchRunLeaseLostException(runId, claim.WorkerInstanceId, claim.LeaseVersion);
        await transaction.CommitAsync(token);
    }

    private IQueryable<ResearchRun> OwnedRun(Guid runId, string owner) => db.ResearchRuns.AsNoTracking().Where(x => x.Id == runId &&
        db.ResearchQuestions.Any(question => question.Id == x.ResearchQuestionId && question.OwnerSubjectId == owner));

    private async Task<bool> IsEligibleAsync(ResearchRun run, string owner, CancellationToken token)
    {
        if (run.Status != ResearchRunStatus.Failed || run.FailedStage != ResearchRunStatus.Synthesizing || run.FailureCategory is null
            || await db.ResearchReports.AnyAsync(x => x.ResearchRunId == run.Id, token)) return false;
        try { await inputs.ValidateAndFingerprintAsync(run.Id, owner, token); return true; }
        catch (Exception error) when (error is SynthesisRetryConflictException or ResearchSynthesisValidationException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private static SynthesisRetryState Project(SynthesisRetryAttemptEntity attempt) => new(attempt.ResearchRunId,
        attempt.Status is "Queued" or "Running" ? "Active" : "Exhausted", attempt.Id, attempt.Status,
        attempt.GenerationBudget, attempt.GenerationCallsReserved, attempt.FailureCategory);
}

using MedResearch.Application.Research;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Application.Security;
using MedResearch.Application.Research.Admission;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Infrastructure.Research;

public sealed class EfResearchStore : IResearchStore
{
    private readonly MedResearchDbContext _dbContext;
    private readonly ResearchAdmissionOptions _admissionOptions;
    private readonly IResearchAdmissionClock _clock;
    private readonly ILogger<EfResearchStore> _logger;

    public EfResearchStore(MedResearchDbContext dbContext)
        : this(dbContext, new ResearchAdmissionOptions(), new PostgreSqlResearchAdmissionClock(), NullLogger<EfResearchStore>.Instance)
    {
    }

    public EfResearchStore(MedResearchDbContext dbContext, ResearchAdmissionOptions admissionOptions,
        IResearchAdmissionClock clock, ILogger<EfResearchStore> logger)
    {
        _dbContext = dbContext;
        admissionOptions.Validate();
        _admissionOptions = admissionOptions;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateResearchResult> PersistInitialResearchAsync(
        ResearchQuestion question,
        ResearchRun run,
        string ownerSubjectId,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);
        if (idempotencyKey == Guid.Empty)
            throw new ResearchAdmissionException(ResearchAdmissionFailure.InvalidKey);
        if (question.Text.Length > 1000)
            throw new ArgumentException("Question must not exceed 1000 characters.", nameof(question));
        if (run.Status != ResearchRunStatus.Queued)
            throw new ArgumentException("New research must be queued.", nameof(run));
        if (!string.Equals(question.OwnerSubjectId, ownerSubjectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Research question ownership does not match the current actor.");
        }

        // Read committed gives the statements after the lock the latest committed admissions.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1297237323, 1)", cancellationToken);
        var fingerprint = ResearchCreateIdentity.Fingerprint(question.Text);
        var existing = await _dbContext.ResearchAdmissions.AsNoTracking()
            .SingleOrDefaultAsync(admission => admission.OwnerSubjectId == ownerSubjectId &&
                admission.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                throw Rejection(ResearchAdmissionFailure.IdempotencyConflict);
            await transaction.CommitAsync(cancellationToken);
            return new CreateResearchResult(existing.ResearchRunId, ResearchRunStatus.Queued.ToString(), Replayed: true);
        }
        if (_admissionOptions.StopNewAdmissions)
            throw Rejection(ResearchAdmissionFailure.Stopped);

        var outstanding = from candidate in _dbContext.ResearchRuns.AsNoTracking()
            join owner in _dbContext.ResearchQuestions.AsNoTracking() on candidate.ResearchQuestionId equals owner.Id
            where candidate.Status != ResearchRunStatus.Completed &&
                candidate.Status != ResearchRunStatus.Failed && candidate.Status != ResearchRunStatus.Cancelled
            select owner.OwnerSubjectId;
        if (await outstanding.CountAsync(owner => owner == ownerSubjectId, cancellationToken) >= _admissionOptions.OwnerOutstandingLimit)
            throw Rejection(ResearchAdmissionFailure.OwnerOutstanding);
        if (await outstanding.CountAsync(cancellationToken) >= _admissionOptions.GlobalOutstandingLimit)
            throw Rejection(ResearchAdmissionFailure.GlobalOutstanding);

        var now = (await _clock.ReadUtcNowAsync(_dbContext, cancellationToken)).ToUniversalTime();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var admissions = _dbContext.ResearchAdmissions.AsNoTracking()
            .Where(admission => admission.CreatedAt >= dayStart && admission.CreatedAt < dayEnd)
            .Select(admission => admission.OwnerSubjectId);
        // Pre-migration runs still count; new daily reservations are independent of run state.
        var legacy = from candidate in _dbContext.ResearchRuns.AsNoTracking()
            join owner in _dbContext.ResearchQuestions.AsNoTracking() on candidate.ResearchQuestionId equals owner.Id
            where candidate.CreatedAt >= dayStart && candidate.CreatedAt < dayEnd &&
                !_dbContext.ResearchAdmissions.Any(admission => admission.ResearchRunId == candidate.Id)
            select owner.OwnerSubjectId;
        var daily = admissions.Concat(legacy);
        var retryAfter = Math.Max(1, (int)Math.Ceiling((dayEnd - now).TotalSeconds));
        if (await daily.CountAsync(owner => owner == ownerSubjectId, cancellationToken) >= _admissionOptions.OwnerDailyLimit)
            throw Rejection(ResearchAdmissionFailure.OwnerDaily, retryAfter);
        if (await daily.CountAsync(cancellationToken) >= _admissionOptions.GlobalDailyLimit)
            throw Rejection(ResearchAdmissionFailure.GlobalDaily, retryAfter);

        _dbContext.ResearchQuestions.Add(question);
        _dbContext.ResearchRuns.Add(run);
        _dbContext.ResearchAdmissions.Add(new ResearchAdmissionEntity
        {
            OwnerSubjectId = ownerSubjectId, IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint, ResearchRunId = run.Id, CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateResearchResult(run.Id, ResearchRunStatus.Queued.ToString());
    }

    private ResearchAdmissionException Rejection(ResearchAdmissionFailure failure, int? retryAfter = null)
    {
        _logger.LogInformation("Research admission rejected. AdmissionOutcome: {AdmissionOutcome}", failure);
        return new ResearchAdmissionException(failure, retryAfter);
    }

    public async Task<ResearchRunDetails?> FindResearchRunAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);
        return await (
            from run in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            where run.Id == researchRunId && question.OwnerSubjectId == ownerSubjectId
            select new ResearchRunDetails(
                run.Id,
                question.Text,
                run.Status.ToString(),
                run.CreatedAt,
                run.StartedAt,
                run.CompletedAt,
                run.FailureReason))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ResearchRunListResult> ListResearchRunsAsync(
        int page,
        int pageSize,
        ResearchRunStatus? status,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);
        var query =
            from run in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            select new
            {
                Run = run,
                Question = question.Text,
                OwnerSubjectId = question.OwnerSubjectId
            };

        query = query.Where(item => item.OwnerSubjectId == ownerSubjectId);

        if (status.HasValue)
        {
            query = query.Where(item => item.Run.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(item => item.Run.CreatedAt)
            .ThenByDescending(item => item.Run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new ResearchRunSummary(
                item.Run.Id,
                item.Run.ResearchQuestionId,
                item.Question,
                item.Run.Status.ToString(),
                item.Run.CreatedAt,
                item.Run.StartedAt,
                item.Run.CompletedAt,
                item.Run.FailureReason))
            .ToArrayAsync(cancellationToken);

        return new ResearchRunListResult(items, page, pageSize, totalCount, totalPages);
    }
}


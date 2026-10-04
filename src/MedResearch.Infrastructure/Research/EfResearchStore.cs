using MedResearch.Application.Research;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Research;

public sealed class EfResearchStore : IResearchStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfResearchStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task PersistInitialResearchAsync(
        ResearchQuestion question,
        ResearchRun run,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);
        if (!string.Equals(question.OwnerSubjectId, ownerSubjectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Research question ownership does not match the current actor.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.ResearchQuestions.Add(question);
        _dbContext.ResearchRuns.Add(run);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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


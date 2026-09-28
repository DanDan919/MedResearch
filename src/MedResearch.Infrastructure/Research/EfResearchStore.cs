using MedResearch.Application.Research;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
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
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.ResearchQuestions.Add(question);
        _dbContext.ResearchRuns.Add(run);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ResearchRunDetails?> FindResearchRunAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        return await (
            from run in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            where run.Id == researchRunId
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
        CancellationToken cancellationToken)
    {
        var query =
            from run in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            select new
            {
                Run = run,
                Question = question.Text
            };

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


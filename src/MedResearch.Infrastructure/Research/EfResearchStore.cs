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
}


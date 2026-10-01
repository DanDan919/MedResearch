using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Planning.Persistence;

public sealed class EfResearchPlanStore : IResearchPlanStore
{
    private readonly MedResearchDbContext _dbContext;
    private readonly IResearchRunWriteFence? _writeFence;

    public EfResearchPlanStore(MedResearchDbContext dbContext, IResearchRunWriteFence? writeFence = null)
    {
        _dbContext = dbContext;
        _writeFence = writeFence;
    }

    public async Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (_writeFence is not null)
        {
            await _writeFence.AssertOwnedAsync(researchPlan.ResearchRunId, cancellationToken);
        }

        _dbContext.ResearchPlans.Add(researchPlan);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        return _dbContext.ResearchPlans
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);
    }
}

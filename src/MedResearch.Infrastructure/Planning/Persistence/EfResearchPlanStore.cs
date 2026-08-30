using MedResearch.Application.Research.Planning;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Planning.Persistence;

public sealed class EfResearchPlanStore : IResearchPlanStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfResearchPlanStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
    {
        _dbContext.ResearchPlans.Add(researchPlan);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        return _dbContext.ResearchPlans
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);
    }
}

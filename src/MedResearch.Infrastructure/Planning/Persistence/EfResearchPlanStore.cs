using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public async Task<ResearchPlan> SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (_writeFence is not null)
        {
            await _writeFence.AssertOwnedAsync(researchPlan.ResearchRunId, cancellationToken);
        }

        var existingPlan = await _dbContext.ResearchPlans
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchPlan.ResearchRunId, cancellationToken);
        if (existingPlan is not null)
        {
            EnsureEquivalent(existingPlan, researchPlan);
            await transaction.CommitAsync(cancellationToken);
            return existingPlan;
        }

        _dbContext.ResearchPlans.Add(researchPlan);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return researchPlan;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            var concurrentPlan = await _dbContext.ResearchPlans
                .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchPlan.ResearchRunId, cancellationToken);
            if (concurrentPlan is null)
            {
                throw;
            }

            EnsureEquivalent(concurrentPlan, researchPlan);
            return concurrentPlan;
        }
    }

    public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        return _dbContext.ResearchPlans
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);
    }

    private static void EnsureEquivalent(ResearchPlan existingPlan, ResearchPlan incomingPlan)
    {
        if (existingPlan.ResearchQuestionId != incomingPlan.ResearchQuestionId
            || !string.Equals(existingPlan.OriginalQuestion, incomingPlan.OriginalQuestion, StringComparison.Ordinal)
            || !string.Equals(existingPlan.Provider, incomingPlan.Provider, StringComparison.Ordinal)
            || !string.Equals(existingPlan.Model, incomingPlan.Model, StringComparison.Ordinal)
            || !string.Equals(existingPlan.PromptVersion, incomingPlan.PromptVersion, StringComparison.Ordinal)
            || !existingPlan.Outcomes.SequenceEqual(incomingPlan.Outcomes, StringComparer.Ordinal)
            || !existingPlan.PreferredStudyTypes.SequenceEqual(incomingPlan.PreferredStudyTypes, StringComparer.Ordinal)
            || !existingPlan.SearchQueries.SequenceEqual(incomingPlan.SearchQueries, StringComparer.Ordinal)
            || !existingPlan.ExclusionHints.SequenceEqual(incomingPlan.ExclusionHints, StringComparer.Ordinal)
            || !string.Equals(existingPlan.Population, incomingPlan.Population, StringComparison.Ordinal)
            || !string.Equals(existingPlan.ExposureOrIntervention, incomingPlan.ExposureOrIntervention, StringComparison.Ordinal)
            || !string.Equals(existingPlan.Comparator, incomingPlan.Comparator, StringComparison.Ordinal))
        {
            throw new ResearchPlanValidationException(
                "A different research plan already exists for this research run.");
        }
    }
}

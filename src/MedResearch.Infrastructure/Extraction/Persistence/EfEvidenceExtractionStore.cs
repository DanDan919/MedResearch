using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Extraction.Persistence;

public sealed class EfEvidenceExtractionStore : IEvidenceExtractionStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfEvidenceExtractionStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EvidenceExtractionWorkItemSet> FindStudiesForExtractionAsync(
        Guid researchRunId,
        string promptVersion,
        int maxStudies,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        if (maxStudies <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStudies), "Max studies must be positive.");
        }

        var plan = await _dbContext.ResearchPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);
        var runAndQuestion = await _dbContext.ResearchRuns
            .AsNoTracking()
            .Join(
                _dbContext.ResearchQuestions.AsNoTracking(),
                run => run.ResearchQuestionId,
                question => question.Id,
                (run, question) => new
                {
                    ResearchRunId = run.Id,
                    ResearchQuestionId = question.Id,
                    ResearchQuestion = question.Text
                })
            .SingleAsync(item => item.ResearchRunId == researchRunId, cancellationToken);

        var baseQuery = _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ResearchRunId == researchRunId)
            .Where(discovery => !_dbContext.EvidenceExtractions.Any(extraction =>
                extraction.ResearchRunId == researchRunId
                && extraction.StudyId == discovery.StudyId
                && extraction.PromptVersion == promptVersion))
            .GroupBy(discovery => discovery.StudyId)
            .Select(group => new
            {
                StudyId = group.Key,
                DiscoveredAt = group.Min(discovery => discovery.DiscoveredAt)
            });

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var discoveredStudies = await baseQuery
            .Join(
                _dbContext.Studies.AsNoTracking(),
                discovery => discovery.StudyId,
                study => study.Id,
                (discovery, study) => new { discovery, study })
            .OrderBy(item => item.discovery.DiscoveredAt)
            .ThenBy(item => item.study.Pmid)
            .ThenBy(item => item.study.Doi)
            .ThenBy(item => item.study.Id)
            .Take(maxStudies)
            .ToArrayAsync(cancellationToken);

        var planContext = plan == null
            ? null
            : new EvidenceExtractionPlanContext(
                plan.Population,
                plan.ExposureOrIntervention,
                plan.Comparator,
                plan.Outcomes,
                plan.PreferredStudyTypes,
                plan.ExclusionHints);

        var studies = discoveredStudies
            .Select(item => new EvidenceExtractionStudyContext(
                researchRunId,
                runAndQuestion.ResearchQuestionId,
                runAndQuestion.ResearchQuestion,
                planContext,
                item.study.Id,
                item.study.Title,
                item.study.Abstract,
                item.study.Pmid,
                item.study.Doi,
                item.study.Journal,
                item.study.PublicationDate,
                item.study.PublicationTypes,
                item.study.Authors,
                item.study.Source))
            .ToArray();

        return new EvidenceExtractionWorkItemSet(totalCount, studies);
    }

    public async Task PersistExtractionResultAsync(
        EvidenceExtractionResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingExtraction = await _dbContext.EvidenceExtractions
            .SingleOrDefaultAsync(extraction =>
                extraction.ResearchRunId == result.ResearchRunId
                && extraction.StudyId == result.StudyId
                && extraction.PromptVersion == result.PromptVersion,
                cancellationToken);

        if (existingExtraction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var extraction = new EvidenceExtraction(
            Guid.NewGuid(),
            result.ResearchRunId,
            result.StudyId,
            result.Status,
            result.SkipReason,
            result.SourceScope,
            result.Provider,
            result.Model,
            result.PromptVersion,
            result.ExtractedAt,
            result.Findings.Count,
            result.GroundingValidated);

        _dbContext.EvidenceExtractions.Add(extraction);

        foreach (var finding in result.Findings)
        {
            _dbContext.Evidence.Add(new Evidence(
                Guid.NewGuid(),
                result.ResearchRunId,
                result.StudyId,
                extraction.Id,
                finding.Outcome,
                finding.ResultSummary,
                finding.SupportingText,
                finding.Direction,
                result.SourceScope,
                result.ExtractedAt,
                result.GroundingValidated,
                finding.Population,
                finding.ExposureOrIntervention,
                finding.Comparator,
                finding.StudyDesign,
                finding.SampleSize,
                finding.EffectMeasure,
                finding.EffectValue,
                finding.ConfidenceIntervalLower,
                finding.ConfidenceIntervalUpper,
                finding.PValue));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

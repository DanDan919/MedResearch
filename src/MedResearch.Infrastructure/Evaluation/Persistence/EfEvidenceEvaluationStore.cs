using MedResearch.Application.Research.Evaluation;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Evaluation.Persistence;

public sealed class EfEvidenceEvaluationStore : IEvidenceEvaluationStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfEvidenceEvaluationStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EvidenceEvaluationWorkItemSet> FindStudiesForEvaluationAsync(
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
        var plan = await _dbContext.ResearchPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);

        var baseQuery = _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(extraction => extraction.ResearchRunId == researchRunId)
            .Where(extraction => !_dbContext.EvidenceEvaluations.Any(evaluation =>
                evaluation.ResearchRunId == researchRunId
                && evaluation.StudyId == extraction.StudyId
                && evaluation.PromptVersion == promptVersion));

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var extractionStudies = await baseQuery
            .Join(
                _dbContext.Studies.AsNoTracking(),
                extraction => extraction.StudyId,
                study => study.Id,
                (extraction, study) => new { extraction, study })
            .OrderBy(item => item.extraction.ExtractedAt)
            .ThenBy(item => item.study.Pmid)
            .ThenBy(item => item.study.Pmcid)
            .ThenBy(item => item.study.Doi)
            .ThenBy(item => item.study.Id)
            .Take(maxStudies)
            .ToArrayAsync(cancellationToken);

        var studyIds = extractionStudies.Select(item => item.study.Id).ToArray();
        var evidenceRows = await _dbContext.Evidence
            .AsNoTracking()
            .Where(evidence => evidence.ResearchRunId == researchRunId && studyIds.Contains(evidence.StudyId))
            .OrderBy(evidence => evidence.ExtractedAt)
            .ThenBy(evidence => evidence.Id)
            .ToArrayAsync(cancellationToken);
        var evidenceByStudyId = evidenceRows
            .GroupBy(evidence => evidence.StudyId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var planContext = plan == null
            ? null
            : new EvaluationPlanContext(
                plan.Population,
                plan.ExposureOrIntervention,
                plan.Comparator,
                plan.Outcomes,
                plan.PreferredStudyTypes,
                plan.ExclusionHints);

        var contexts = extractionStudies.Select(item =>
        {
            evidenceByStudyId.TryGetValue(item.study.Id, out var studyEvidence);
            studyEvidence ??= [];

            return new EvaluationStudyContext(
                researchRunId,
                runAndQuestion.ResearchQuestionId,
                runAndQuestion.ResearchQuestion,
                planContext,
                item.study.Id,
                item.study.Title,
                item.study.Abstract,
                item.study.Pmid,
                item.study.Pmcid,
                item.study.Doi,
                item.study.Journal,
                item.study.PublicationDate,
                item.study.PublicationTypes,
                item.study.Authors,
                item.study.Source,
                item.extraction.Status,
                item.extraction.SkipReason,
                item.extraction.SourceScope,
                item.extraction.PromptVersion,
                studyEvidence.Select(ToEvidenceContext).ToArray());
        }).ToArray();

        return new EvidenceEvaluationWorkItemSet(totalCount, contexts);
    }

    public async Task PersistEvaluationResultAsync(
        EvidenceEvaluationResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingEvaluation = await _dbContext.EvidenceEvaluations
            .SingleOrDefaultAsync(evaluation =>
                evaluation.ResearchRunId == result.ResearchRunId
                && evaluation.StudyId == result.StudyId
                && evaluation.PromptVersion == result.PromptVersion,
                cancellationToken);

        if (existingEvaluation is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        _dbContext.EvidenceEvaluations.Add(new EvidenceEvaluation(
            Guid.NewGuid(),
            result.ResearchRunId,
            result.StudyId,
            result.EvidenceIds.ToArray(),
            result.Status,
            result.SkipReason,
            result.SourceScope,
            result.EvaluatorProvider,
            result.EvaluatorModel,
            result.PromptVersion,
            result.EvaluatedAt,
            result.StudyDesign,
            result.SampleInformation,
            result.ComparatorPresence,
            result.ComparatorDescription,
            result.Randomization,
            result.Blinding,
            result.AllocationConcealment,
            result.AttritionMissingData,
            result.Precision,
            result.Directness,
            result.OverallConfidence,
            result.Rationale,
            result.ReportingLimitations.ToArray(),
            result.AuthorReportedLimitations.ToArray(),
            result.Signals.HasSampleSize,
            result.Signals.HasEffectEstimate,
            result.Signals.HasConfidenceInterval,
            result.Signals.HasPValue,
            result.Signals.HasComparator,
            result.UnknownDomainCount,
            result.InsufficientSourceDomainCount));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static EvaluationEvidenceContext ToEvidenceContext(Evidence evidence)
    {
        return new EvaluationEvidenceContext(
            evidence.Id,
            evidence.Outcome,
            evidence.ResultSummary,
            evidence.SupportingText,
            evidence.Direction,
            evidence.Population,
            evidence.ExposureOrIntervention,
            evidence.Comparator,
            evidence.StudyDesign,
            evidence.SampleSize,
            evidence.EffectMeasure,
            evidence.EffectValue,
            evidence.ConfidenceIntervalLower,
            evidence.ConfidenceIntervalUpper,
            evidence.PValue,
            evidence.GroundingValidated);
    }
}

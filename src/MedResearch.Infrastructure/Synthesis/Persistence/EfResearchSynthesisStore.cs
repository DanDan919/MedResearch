using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class EfResearchSynthesisStore : ISynthesisCorpusStore, IResearchReportStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfResearchSynthesisStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
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
        var planContext = plan is null
            ? null
            : new SynthesisPlanContext(
                plan.Id,
                plan.Population,
                plan.ExposureOrIntervention,
                plan.Comparator,
                plan.Outcomes,
                plan.PreferredStudyTypes,
                plan.SearchQueries,
                plan.ExclusionHints);

        var searches = await _dbContext.LiteratureSearches
            .AsNoTracking()
            .Where(search => search.ResearchRunId == researchRunId)
            .OrderBy(search => search.SearchedAt)
            .ThenBy(search => search.Id)
            .Select(search => new SynthesisSearchSnapshot(
                search.Id,
                search.ResearchRunId,
                search.Source,
                search.Query,
                search.SearchedAt,
                search.ResultCount,
                search.PersistedStudyCount,
                search.DuplicateStudyCount))
            .ToArrayAsync(cancellationToken);
        var discoveredStudies = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ResearchRunId == researchRunId)
            .GroupBy(discovery => discovery.StudyId)
            .Select(group => new
            {
                StudyId = group.Key,
                DiscoveredAt = group.Min(discovery => discovery.DiscoveredAt)
            })
            .Join(
                _dbContext.Studies.AsNoTracking(),
                discovery => discovery.StudyId,
                study => study.Id,
                (discovery, study) => new { discovery, study })
            .OrderBy(item => item.discovery.DiscoveredAt)
            .ThenBy(item => item.study.Pmid)
            .ThenBy(item => item.study.Pmcid)
            .ThenBy(item => item.study.Doi)
            .ThenBy(item => item.study.Id)
            .Select(item => new SynthesisStudySnapshot(
                item.study.Id,
                item.study.Title,
                item.study.Pmid,
                item.study.Pmcid,
                item.study.Doi,
                item.study.Journal,
                item.study.PublicationDate,
                item.study.PublicationTypes,
                item.study.Authors,
                item.study.Source,
                item.discovery.DiscoveredAt))
            .ToArrayAsync(cancellationToken);
        var studyIds = discoveredStudies.Select(study => study.StudyId).ToArray();
        var evidence = await _dbContext.Evidence
            .AsNoTracking()
            .Where(evidence => evidence.ResearchRunId == researchRunId)
            .Where(evidence => studyIds.Contains(evidence.StudyId))
            .Where(evidence => evidence.GroundingValidated)
            .OrderBy(evidence => evidence.ExtractedAt)
            .ThenBy(evidence => evidence.StudyId)
            .ThenBy(evidence => evidence.Id)
            .Select(evidence => new SynthesisEvidenceContext(
                evidence.Id,
                evidence.ResearchRunId,
                evidence.StudyId,
                evidence.Outcome,
                evidence.ResultSummary,
                evidence.SupportingText,
                evidence.Direction,
                evidence.SourceScope,
                evidence.ExtractedAt,
                evidence.Population,
                evidence.ExposureOrIntervention,
                evidence.Comparator,
                evidence.StudyDesign,
                evidence.SampleSize,
                evidence.EffectMeasure,
                evidence.EffectValue,
                evidence.ConfidenceIntervalLower,
                evidence.ConfidenceIntervalUpper,
                evidence.PValue))
            .ToArrayAsync(cancellationToken);
        var evaluations = await _dbContext.EvidenceEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.ResearchRunId == researchRunId)
            .Where(evaluation => studyIds.Contains(evaluation.StudyId))
            .OrderBy(evaluation => evaluation.EvaluatedAt)
            .ThenBy(evaluation => evaluation.StudyId)
            .ThenBy(evaluation => evaluation.Id)
            .Select(evaluation => new SynthesisEvaluationContext(
                evaluation.Id,
                evaluation.ResearchRunId,
                evaluation.StudyId,
                evaluation.Status,
                evaluation.SkipReason,
                evaluation.SourceScope,
                evaluation.StudyDesign,
                evaluation.SampleInformation,
                evaluation.ComparatorPresence,
                evaluation.Randomization,
                evaluation.Blinding,
                evaluation.AllocationConcealment,
                evaluation.AttritionMissingData,
                evaluation.Precision,
                evaluation.Directness,
                evaluation.OverallConfidence,
                evaluation.EvidenceIds,
                evaluation.ReportingLimitations,
                evaluation.UnknownDomainCount,
                evaluation.InsufficientSourceDomainCount))
            .ToArrayAsync(cancellationToken);
        var extractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(extraction => extraction.ResearchRunId == researchRunId)
            .Where(extraction => studyIds.Contains(extraction.StudyId))
            .OrderBy(extraction => extraction.ExtractedAt)
            .ThenBy(extraction => extraction.StudyId)
            .ThenBy(extraction => extraction.Id)
            .Select(extraction => new SynthesisExtractionSnapshot(
                extraction.Id,
                extraction.ResearchRunId,
                extraction.StudyId,
                extraction.Status,
                extraction.SkipReason,
                extraction.SourceScope,
                extraction.EvidenceCount,
                extraction.GroundingValidated))
            .ToArrayAsync(cancellationToken);

        return new SynthesisCorpusSnapshot(
            runAndQuestion.ResearchRunId,
            runAndQuestion.ResearchQuestionId,
            runAndQuestion.ResearchQuestion,
            planContext,
            discoveredStudies,
            evidence,
            evaluations,
            searches,
            extractions);
    }

    public async Task<bool> HasReportAsync(Guid researchRunId, string promptVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        return await _dbContext.ResearchReports
            .AsNoTracking()
            .AnyAsync(report => report.ResearchRunId == researchRunId && report.PromptVersion == promptVersion, cancellationToken);
    }

    public async Task PersistReportAsync(ResearchSynthesisResult result, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingReport = await _dbContext.ResearchReports
            .SingleOrDefaultAsync(report => report.ResearchRunId == result.ResearchRunId && report.PromptVersion == result.PromptVersion, cancellationToken);
        if (existingReport is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var reportId = Guid.NewGuid();
        var report = new ResearchReport(
            reportId,
            result.ResearchRunId,
            result.Status,
            result.InsufficientEvidenceReason,
            result.ExecutiveSummary,
            result.EvidenceSummary,
            result.ConflictSummary,
            result.LimitationsSummary,
            result.Conclusion,
            result.SynthesisConfidence,
            result.SynthesizerProvider,
            result.SynthesizerModel,
            result.PromptVersion,
            result.GeneratedAt,
            result.Statistics.DiscoveredStudyCount,
            result.Statistics.ExtractedStudyCount,
            result.Statistics.EvaluatedStudyCount,
            result.Statistics.EvidenceFindingCount,
            result.Statistics.IncludedStudyCount,
            result.Statistics.IncludedEvidenceFindingCount,
            result.Claims.Count,
            result.Statistics.SearchQueryCount,
            result.Statistics.StudiesWithNoExtractableEvidence,
            result.Statistics.StudiesWithInsufficientEvaluationSource,
            result.SourceCoverage.PotentialConflictDetected,
            result.SourceCoverage.EvidenceTruncated,
            result.SourceCoverage.UsesAbstractLevelEvidenceOnly,
            result.SourceCoverage.SearchedSources.ToArray(),
            result.DeterministicLimitations.ToArray());
        _dbContext.ResearchReports.Add(report);

        foreach (var acceptedClaim in result.Claims.OrderBy(claim => claim.Ordinal))
        {
            var claimId = Guid.NewGuid();
            _dbContext.ResearchReportClaims.Add(new ResearchReportClaim(
                claimId,
                reportId,
                acceptedClaim.ClaimType,
                acceptedClaim.Direction,
                acceptedClaim.Text,
                acceptedClaim.Ordinal));

            var citationOrdinal = 0;
            foreach (var evidenceId in acceptedClaim.EvidenceIds.Distinct())
            {
                _dbContext.ResearchReportClaimEvidence.Add(new ResearchReportClaimEvidence(
                    claimId,
                    evidenceId,
                    citationOrdinal++));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ResearchReportReadModel?> FindReportAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var reportProjection = await (
            from report in _dbContext.ResearchReports.AsNoTracking()
            join run in _dbContext.ResearchRuns.AsNoTracking()
                on report.ResearchRunId equals run.Id
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            where report.ResearchRunId == researchRunId
            orderby report.GeneratedAt descending, report.Id
            select new { report, question.Text })
            .FirstOrDefaultAsync(cancellationToken);

        if (reportProjection is null)
        {
            return null;
        }

        var reportEntity = reportProjection.report;
        var claims = await _dbContext.ResearchReportClaims
            .AsNoTracking()
            .Where(claim => claim.ResearchReportId == reportEntity.Id)
            .OrderBy(claim => claim.Ordinal)
            .ThenBy(claim => claim.Id)
            .ToArrayAsync(cancellationToken);
        var claimIds = claims.Select(claim => claim.Id).ToArray();
        var citationRows = await _dbContext.ResearchReportClaimEvidence
            .AsNoTracking()
            .Where(link => claimIds.Contains(link.ResearchReportClaimId))
            .Join(
                _dbContext.Evidence.AsNoTracking(),
                link => link.EvidenceId,
                evidence => evidence.Id,
                (link, evidence) => new { link, evidence })
            .Join(
                _dbContext.Studies.AsNoTracking(),
                item => item.evidence.StudyId,
                study => study.Id,
                (item, study) => new
                {
                    item.link.ResearchReportClaimId,
                    item.link.Ordinal,
                    Evidence = item.evidence,
                    Study = study
                })
            .OrderBy(item => item.ResearchReportClaimId)
            .ThenBy(item => item.Ordinal)
            .ToArrayAsync(cancellationToken);
        var citationsByClaimId = citationRows
            .GroupBy(row => row.ResearchReportClaimId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<ResearchReportCitationReadModel>)group
                    .OrderBy(row => row.Ordinal)
                    .Select(row => new ResearchReportCitationReadModel(
                        row.Evidence.Id,
                        row.Study.Id,
                        row.Study.Pmid,
                        row.Study.Pmcid,
                        row.Study.Doi,
                        row.Study.Title,
                        row.Evidence.SupportingText,
                        row.Evidence.Direction,
                        row.Ordinal))
                    .ToArray());

        var claimModels = claims.Select(claim =>
        {
            citationsByClaimId.TryGetValue(claim.Id, out var citations);
            citations ??= [];

            return new ResearchReportClaimReadModel(
                claim.Id,
                claim.ClaimType,
                claim.Direction,
                claim.Text,
                claim.Ordinal,
                citations);
        }).ToArray();
        var coverage = new ResearchReportCoverageReadModel(
            reportEntity.DiscoveredStudyCount,
            reportEntity.ExtractedStudyCount,
            reportEntity.EvaluatedStudyCount,
            reportEntity.EvidenceFindingCount,
            reportEntity.IncludedStudyCount,
            reportEntity.IncludedEvidenceFindingCount,
            reportEntity.SearchQueryCount,
            reportEntity.StudiesWithNoExtractableEvidence,
            reportEntity.StudiesWithInsufficientEvaluationSource,
            reportEntity.PotentialConflictDetected,
            reportEntity.EvidenceTruncated,
            reportEntity.UsesAbstractLevelEvidenceOnly,
            reportEntity.SearchedSources);

        return new ResearchReportReadModel(
            reportEntity.ResearchRunId,
            reportEntity.Id,
            reportEntity.Status,
            reportEntity.InsufficientEvidenceReason,
            reportProjection.Text,
            reportEntity.ExecutiveSummary,
            reportEntity.EvidenceSummary,
            reportEntity.ConflictSummary,
            reportEntity.LimitationsSummary,
            reportEntity.Conclusion,
            reportEntity.SynthesisConfidence,
            reportEntity.PromptVersion,
            reportEntity.GeneratedAt,
            coverage,
            reportEntity.DeterministicLimitations,
            claimModels);
    }
}

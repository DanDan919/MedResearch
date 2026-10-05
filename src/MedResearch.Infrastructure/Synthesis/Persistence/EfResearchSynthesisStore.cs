using MedResearch.Application.Research.Synthesis;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MedResearch.Application.Security;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class EfResearchSynthesisStore : ISynthesisCorpusStore, IResearchReportStore
{
    private readonly MedResearchDbContext _dbContext;
    private readonly IResearchRunWriteFence? _writeFence;

    public EfResearchSynthesisStore(MedResearchDbContext dbContext, IResearchRunWriteFence? writeFence = null)
    {
        _dbContext = dbContext;
        _writeFence = writeFence;
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
        var evidenceRows = await _dbContext.Evidence
            .AsNoTracking()
            .Where(evidence => evidence.ResearchRunId == researchRunId)
            .Where(evidence => studyIds.Contains(evidence.StudyId))
            .Where(evidence => evidence.GroundingValidated)
            .OrderBy(evidence => evidence.ExtractedAt)
            .ThenBy(evidence => evidence.StudyId)
            .ThenBy(evidence => evidence.Id)
            .ToArrayAsync(cancellationToken);
        var evidence = evidenceRows
            .Select(evidence => new SynthesisEvidenceContext(
                evidence.Id,
                evidence.ResearchRunId,
                evidence.StudyId,
                evidence.EvidenceExtractionId,
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
                evidence.PValue,
                evidence.ConfidenceLevel,
                evidence.ReportedStandardError,
                evidence.PValueOperator,
                evidence.NumericGrounding))
            .ToArray();
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
                extraction.SourceMaterialId,
                extraction.EvidenceCount,
                extraction.GroundingValidated))
            .ToArrayAsync(cancellationToken);
        var sourceMaterials = await _dbContext.SourceMaterials
            .AsNoTracking()
            .Where(material => studyIds.Contains(material.StudyId))
            .OrderBy(material => material.StudyId)
            .ThenBy(material => material.Type)
            .ThenBy(material => material.Provider)
            .ThenByDescending(material => material.ContentVersion)
            .ThenBy(material => material.Id)
            .Select(material => new SynthesisSourceMaterialSnapshot(
                material.Id,
                material.StudyId,
                material.Type,
                material.Provider,
                material.ProviderSourceId,
                material.ContentHash,
                material.ContentVersion,
                material.WasTruncated,
                material.IsCurrent))
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
            extractions,
            sourceMaterials);
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
        if (_writeFence is not null)
        {
            await _writeFence.AssertOwnedAsync(result.ResearchRunId, cancellationToken);
        }

        var existingReport = await _dbContext.ResearchReports
            .SingleOrDefaultAsync(report => report.ResearchRunId == result.ResearchRunId && report.PromptVersion == result.PromptVersion, cancellationToken);
        if (existingReport is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await ValidateReportCitationsAsync(result, cancellationToken);

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
            result.Statistics.StructuredFullTextStudyCount,
            result.Statistics.AbstractOnlyStudyCount,
            result.Statistics.NoSourceMaterialStudyCount,
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

    private async Task ValidateReportCitationsAsync(
        ResearchSynthesisResult result,
        CancellationToken cancellationToken)
    {
        if (result.Claims.Any(claim => claim.EvidenceIds.Count == 0))
        {
            throw new InvalidOperationException("Every persisted research report claim must cite at least one Evidence row.");
        }

        var evidenceIds = result.Claims
            .SelectMany(claim => claim.EvidenceIds)
            .Distinct()
            .ToArray();
        if (evidenceIds.Length == 0)
        {
            return;
        }

        var evidence = await _dbContext.Evidence
            .AsNoTracking()
            .Where(item => evidenceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var extractionIds = evidence.Values.Select(item => item.EvidenceExtractionId).Distinct().ToArray();
        var extractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(item => extractionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var sourceMaterialIds = extractions.Values
            .Where(item => item.SourceMaterialId.HasValue)
            .Select(item => item.SourceMaterialId!.Value)
            .Distinct()
            .ToArray();
        var sourceMaterials = await _dbContext.SourceMaterials
            .AsNoTracking()
            .Where(item => sourceMaterialIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var evidenceId in evidenceIds)
        {
            if (!evidence.TryGetValue(evidenceId, out var item)
                || item.ResearchRunId != result.ResearchRunId
                || !item.GroundingValidated
                || !extractions.TryGetValue(item.EvidenceExtractionId, out var extraction)
                || extraction.ResearchRunId != result.ResearchRunId
                || extraction.StudyId != item.StudyId
                || extraction.Status != EvidenceExtractionStatus.Completed
                || !extraction.GroundingValidated
                || !extraction.SourceMaterialId.HasValue
                || !sourceMaterials.TryGetValue(extraction.SourceMaterialId.Value, out var sourceMaterial)
                || sourceMaterial.StudyId != item.StudyId)
            {
                throw new InvalidOperationException(
                    $"Research report citation {evidenceId} does not resolve to grounded, same-run Evidence and SourceMaterial lineage.");
            }
        }
    }

    public async Task<ResearchReportReadModel?> FindReportAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);
        var reportProjection = await (
            from report in _dbContext.ResearchReports.AsNoTracking()
            join run in _dbContext.ResearchRuns.AsNoTracking()
                on report.ResearchRunId equals run.Id
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on run.ResearchQuestionId equals question.Id
            where report.ResearchRunId == researchRunId && question.OwnerSubjectId == ownerSubjectId
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
                _dbContext.EvidenceExtractions.AsNoTracking(),
                item => item.evidence.EvidenceExtractionId,
                extraction => extraction.Id,
                (item, extraction) => new { item.link, item.evidence, extraction })
            .Join(
                _dbContext.Studies.AsNoTracking(),
                item => item.evidence.StudyId,
                study => study.Id,
                (item, study) => new { item.link, item.evidence, item.extraction, study })
            .GroupJoin(
                _dbContext.SourceMaterials.AsNoTracking(),
                item => item.extraction.SourceMaterialId,
                sourceMaterial => sourceMaterial.Id,
                (item, sourceMaterials) => new { item.link, item.evidence, item.extraction, item.study, sourceMaterials })
            .SelectMany(
                item => item.sourceMaterials.DefaultIfEmpty(),
                (item, sourceMaterial) => new
                {
                    item.link.ResearchReportClaimId,
                    item.link.Ordinal,
                    Evidence = item.evidence,
                    Extraction = item.extraction,
                    Study = item.study,
                    SourceMaterial = sourceMaterial
                })
            .Where(item => item.Evidence.ResearchRunId == reportEntity.ResearchRunId
                && item.Extraction.ResearchRunId == reportEntity.ResearchRunId
                && item.Extraction.StudyId == item.Evidence.StudyId
                && item.Evidence.GroundingValidated
                && item.Extraction.Status == EvidenceExtractionStatus.Completed
                && item.Extraction.GroundingValidated
                && item.SourceMaterial != null
                && item.SourceMaterial.StudyId == item.Study.Id)
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
                        row.Study.Journal,
                        row.Study.PublicationYear,
                        row.Study.PublicationMonth,
                        row.Study.PublicationDay,
                        row.Study.PublicationTypes,
                        row.Study.Authors,
                        row.Study.Source,
                        row.Evidence.Outcome,
                        row.Evidence.ResultSummary,
                        row.Evidence.SupportingText,
                        row.Evidence.Direction,
                        row.Evidence.SourceScope,
                        row.Evidence.GroundingValidated,
                        row.Evidence.Population,
                        row.Evidence.ExposureOrIntervention,
                        row.Evidence.Comparator,
                        row.Evidence.StudyDesign,
                        row.Evidence.SampleSize,
                        row.Evidence.EffectMeasure,
                        row.Evidence.EffectValue,
                        row.Evidence.ConfidenceIntervalLower,
                        row.Evidence.ConfidenceIntervalUpper,
                        row.Evidence.ConfidenceLevel,
                        row.Evidence.ReportedStandardError,
                        row.Evidence.PValue,
                        row.Evidence.ExtractedAt,
                        row.SourceMaterial is null
                            ? null
                            : new ResearchReportSourceMaterialReadModel(
                                row.SourceMaterial.Id,
                                row.SourceMaterial.Type.ToString(),
                                row.SourceMaterial.Provider,
                                row.SourceMaterial.RetrievalMethod,
                                row.SourceMaterial.ContentVersion,
                                row.SourceMaterial.RetrievedAt,
                                row.SourceMaterial.AccessStatus.ToString(),
                                row.SourceMaterial.WasTruncated,
                                row.SourceMaterial.SectionNames),
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
            reportEntity.StructuredFullTextStudyCount,
            reportEntity.AbstractOnlyStudyCount,
            reportEntity.NoSourceMaterialStudyCount,
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

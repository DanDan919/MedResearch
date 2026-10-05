using MedResearch.Application.Research.Provenance;
using MedResearch.Application.Security;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Research;

public sealed class EfResearchProvenanceStore : IResearchProvenanceStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfResearchProvenanceStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ResearchProvenanceReadModel?> FindAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);

        var run = await (
            from researchRun in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on researchRun.ResearchQuestionId equals question.Id
            where researchRun.Id == researchRunId && question.OwnerSubjectId == ownerSubjectId
            select new
            {
                Run = researchRun,
                Question = question.Text
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return null;
        }

        var plans = await _dbContext.ResearchPlans
            .AsNoTracking()
            .Where(plan => plan.ResearchRunId == researchRunId)
            .OrderBy(plan => plan.GeneratedAt)
            .ThenBy(plan => plan.Id)
            .Select(plan => new ResearchPlanProvenance(
                plan.Id,
                plan.OriginalQuestion,
                plan.SearchQueries,
                plan.Provider,
                plan.Model,
                plan.PromptVersion,
                plan.GeneratedAt))
            .ToArrayAsync(cancellationToken);

        var searches = await _dbContext.LiteratureSearches
            .AsNoTracking()
            .Where(search => search.ResearchRunId == researchRunId)
            .OrderBy(search => search.SearchedAt)
            .ThenBy(search => search.Id)
            .Select(search => new LiteratureSearchProvenance(
                search.Id,
                search.ResearchPlanId,
                search.Source,
                search.Query,
                search.SearchedAt,
                search.ResultCount,
                search.PersistedStudyCount,
                search.DuplicateStudyCount,
                search.ResultCount == 0 ? "SucceededWithZeroResults" : "SucceededWithResults"))
            .ToArrayAsync(cancellationToken);

        var discoveryRows = await (
            from discovery in _dbContext.ResearchStudyDiscoveries.AsNoTracking()
            join search in _dbContext.LiteratureSearches.AsNoTracking()
                on discovery.LiteratureSearchId equals search.Id
            where discovery.ResearchRunId == researchRunId
            orderby discovery.DiscoveredAt, discovery.Id
            select new
            {
                Discovery = discovery,
                Search = search
            })
            .ToArrayAsync(cancellationToken);

        var studyIds = discoveryRows
            .Select(row => row.Discovery.StudyId)
            .Distinct()
            .ToArray();

        var studies = await _dbContext.Studies
            .AsNoTracking()
            .Where(study => studyIds.Contains(study.Id))
            .OrderBy(study => study.Title)
            .ThenBy(study => study.Id)
            .ToArrayAsync(cancellationToken);

        var sourceMaterials = await _dbContext.SourceMaterials
            .AsNoTracking()
            .Where(material => studyIds.Contains(material.StudyId))
            .OrderBy(material => material.StudyId)
            .ThenBy(material => material.ContentVersion)
            .ThenBy(material => material.Id)
            .Select(material => new SourceMaterialProvenance(
                material.Id,
                material.StudyId,
                material.Type,
                material.Provider,
                material.ProviderSourceId,
                material.RetrievalMethod,
                material.ContentHash,
                material.ContentVersion,
                material.RetrievedAt,
                material.SourceUpdatedAt,
                material.AccessStatus,
                material.CharacterCount,
                material.WasTruncated,
                material.IsCurrent,
                material.SectionNames))
            .ToArrayAsync(cancellationToken);

        var extractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(extraction => extraction.ResearchRunId == researchRunId)
            .OrderBy(extraction => extraction.ExtractedAt)
            .ThenBy(extraction => extraction.Id)
            .Select(extraction => new EvidenceExtractionProvenance(
                extraction.Id,
                extraction.StudyId,
                extraction.SourceMaterialId,
                extraction.Status,
                extraction.SkipReason,
                extraction.SourceScope,
                extraction.Provider,
                extraction.Model,
                extraction.PromptVersion,
                extraction.ExtractedAt,
                extraction.EvidenceCount,
                extraction.GroundingValidated))
            .ToArrayAsync(cancellationToken);

        var evidence = await _dbContext.Evidence
            .AsNoTracking()
            .Where(item => item.ResearchRunId == researchRunId)
            .OrderBy(item => item.ExtractedAt)
            .ThenBy(item => item.Id)
            .Select(item => new EvidenceProvenance(
                item.Id,
                item.EvidenceExtractionId,
                item.Outcome,
                item.ResultSummary,
                item.SupportingText,
                item.Direction,
                item.SourceScope,
                item.ExtractedAt,
                item.GroundingValidated,
                item.Population,
                item.ExposureOrIntervention,
                item.Comparator,
                item.StudyDesign,
                item.SampleSize,
                item.EffectMeasure,
                item.EffectValue,
                item.ConfidenceIntervalLower,
                item.ConfidenceIntervalUpper,
                item.ConfidenceLevel,
                item.ReportedStandardError,
                item.PValue))
            .ToArrayAsync(cancellationToken);

        var evaluations = await _dbContext.EvidenceEvaluations
            .AsNoTracking()
            .Where(item => item.ResearchRunId == researchRunId)
            .OrderBy(item => item.EvaluatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new EvidenceEvaluationProvenance(
                item.Id,
                item.StudyId,
                item.Status,
                item.SkipReason,
                item.SourceScope,
                item.EvidenceIds,
                item.EvaluatorProvider,
                item.EvaluatorModel,
                item.PromptVersion,
                item.EvaluatedAt,
                item.StudyDesign,
                item.SampleInformation,
                item.ComparatorPresence,
                item.ComparatorDescription,
                item.Randomization,
                item.Blinding,
                item.AllocationConcealment,
                item.AttritionMissingData,
                item.Precision,
                item.Directness,
                item.OverallConfidence,
                item.Rationale,
                item.ReportingLimitations,
                item.AuthorReportedLimitations,
                item.HasSampleSize,
                item.HasEffectEstimate,
                item.HasConfidenceInterval,
                item.HasPValue,
                item.HasComparator,
                item.UnknownDomainCount,
                item.InsufficientSourceDomainCount))
            .ToArrayAsync(cancellationToken);

        var claims = await (
            from report in _dbContext.ResearchReports.AsNoTracking()
            join claim in _dbContext.ResearchReportClaims.AsNoTracking()
                on report.Id equals claim.ResearchReportId
            where report.ResearchRunId == researchRunId
            select new
            {
                ReportId = report.Id,
                Claim = claim
            })
            .OrderBy(item => item.Claim.Ordinal)
            .ToArrayAsync(cancellationToken);

        var claimIds = claims.Select(item => item.Claim.Id).ToArray();
        var claimEvidenceLinks = await (
            from link in _dbContext.ResearchReportClaimEvidence.AsNoTracking()
            join item in _dbContext.Evidence.AsNoTracking()
                on link.EvidenceId equals item.Id
            where claimIds.Contains(link.ResearchReportClaimId)
                && item.ResearchRunId == researchRunId
            orderby link.Ordinal, link.EvidenceId
            select new
            {
                link.ResearchReportClaimId,
                link.EvidenceId
            })
            .ToArrayAsync(cancellationToken);

        var evidenceIdsByClaim = claimEvidenceLinks
            .GroupBy(link => link.ResearchReportClaimId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<Guid>)group.Select(link => link.EvidenceId).ToArray());

        var claimReadModels = claims
            .Select(item => new ResearchReportClaimProvenance(
                item.ReportId,
                item.Claim.Id,
                item.Claim.ClaimType,
                item.Claim.Direction,
                item.Claim.Text,
                item.Claim.Ordinal,
                evidenceIdsByClaim.GetValueOrDefault(item.Claim.Id, [])))
            .OrderBy(claim => claim.Ordinal)
            .ThenBy(claim => claim.ResearchReportClaimId)
            .ToArray();

        var quantitativeContributions = await (
            from artifact in _dbContext.QuantitativeSynthesisArtifacts.AsNoTracking()
            join snapshot in _dbContext.QuantitativeSynthesisContributionSnapshots.AsNoTracking()
                on artifact.Id equals snapshot.ArtifactId
            where artifact.ResearchRunId == researchRunId
            orderby artifact.GroupKey, snapshot.AnalysisMethod, snapshot.Ordinal
            select new QuantitativeContributionProvenance(
                artifact.Id,
                artifact.GroupKey,
                snapshot.AnalysisMethod,
                snapshot.Ordinal,
                snapshot.EvidenceId,
                snapshot.StudyId,
                snapshot.EvidenceExtractionId,
                snapshot.SourceMaterialId))
            .ToArrayAsync(cancellationToken);

        var discoveriesByStudy = discoveryRows
            .GroupBy(row => row.Discovery.StudyId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<StudyDiscoveryProvenance>)group
                    .Select(row => new StudyDiscoveryProvenance(
                        row.Discovery.Id,
                        row.Discovery.LiteratureSearchId,
                        row.Discovery.Source,
                        row.Discovery.SourceStudyIdentifier,
                        row.Search.Query,
                        row.Search.SearchedAt,
                        row.Discovery.DiscoveredAt))
                    .ToArray());

        var sourceMaterialsByStudy = sourceMaterials
            .GroupBy(material => material.StudyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<SourceMaterialProvenance>)group.ToArray());
        var extractionsByStudy = extractions
            .GroupBy(extraction => extraction.StudyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<EvidenceExtractionProvenance>)group.ToArray());
        var evidenceByExtraction = evidence
            .GroupBy(item => item.EvidenceExtractionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<EvidenceProvenance>)group.ToArray());
        var evaluationsByStudy = evaluations
            .GroupBy(evaluation => evaluation.StudyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<EvidenceEvaluationProvenance>)group.ToArray());

        var studyReadModels = studies
            .Select(study => new StudyProvenance(
                study.Id,
                study.Title,
                study.Pmid,
                study.Pmcid,
                study.Doi,
                study.Journal,
                study.PublicationYear,
                study.PublicationMonth,
                study.PublicationDay,
                study.PublicationTypes,
                study.Authors,
                study.Source,
                discoveriesByStudy.GetValueOrDefault(study.Id, []),
                sourceMaterialsByStudy.GetValueOrDefault(study.Id, []),
                extractionsByStudy.GetValueOrDefault(study.Id, []),
                extractionsByStudy.GetValueOrDefault(study.Id, [])
                    .SelectMany(extraction => evidenceByExtraction.GetValueOrDefault(extraction.EvidenceExtractionId, []))
                    .ToArray(),
                evaluationsByStudy.GetValueOrDefault(study.Id, [])))
            .ToArray();

        return new ResearchProvenanceReadModel(
            researchRunId,
            run.Question,
            run.Run.Status,
            run.Run.CreatedAt,
            run.Run.StartedAt,
            run.Run.CompletedAt,
            new ResearchProvenanceCoverage(
                plans.Length,
                searches.Length,
                discoveryRows.Length,
                studyReadModels.Length,
                sourceMaterials.Length,
                extractions.Length,
                evidence.Length,
                evaluations.Length,
                claimReadModels.Length,
                false),
            plans,
            searches,
            studyReadModels,
            claimReadModels,
            quantitativeContributions);
    }
}

using MedResearch.Application.Research;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Research;

public sealed class EfResearchProgressStore : IResearchProgressStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfResearchProgressStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ResearchRunProgressSnapshot?> FindResearchRunProgressSnapshotAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var run = await (
            from researchRun in _dbContext.ResearchRuns.AsNoTracking()
            join question in _dbContext.ResearchQuestions.AsNoTracking()
                on researchRun.ResearchQuestionId equals question.Id
            where researchRun.Id == researchRunId
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
            .Select(plan => plan.SearchQueries)
            .ToArrayAsync(cancellationToken);

        var searches = await _dbContext.LiteratureSearches
            .AsNoTracking()
            .Where(search => search.ResearchRunId == researchRunId)
            .Select(search => new
            {
                search.Source,
                search.ResultCount
            })
            .ToArrayAsync(cancellationToken);

        var discoveredStudyIds = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ResearchRunId == researchRunId)
            .Select(discovery => discovery.StudyId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var discoveryPathCount = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .CountAsync(discovery => discovery.ResearchRunId == researchRunId, cancellationToken);

        var sourceMaterials = discoveredStudyIds.Length == 0
            ? Array.Empty<SourceMaterialType>()
            : await _dbContext.SourceMaterials
                .AsNoTracking()
                .Where(material => discoveredStudyIds.Contains(material.StudyId) && material.IsCurrent)
                .Select(material => material.Type)
                .ToArrayAsync(cancellationToken);

        var extractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(extraction => extraction.ResearchRunId == researchRunId)
            .Select(extraction => extraction.Status)
            .ToArrayAsync(cancellationToken);

        var evaluations = await _dbContext.EvidenceEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.ResearchRunId == researchRunId)
            .Select(evaluation => evaluation.Status)
            .ToArrayAsync(cancellationToken);

        var reportIds = await _dbContext.ResearchReports
            .AsNoTracking()
            .Where(report => report.ResearchRunId == researchRunId)
            .Select(report => report.Id)
            .ToArrayAsync(cancellationToken);

        var claimCount = reportIds.Length == 0
            ? 0
            : await _dbContext.ResearchReportClaims
                .AsNoTracking()
                .CountAsync(claim => reportIds.Contains(claim.ResearchReportId), cancellationToken);

        var metrics = new ResearchRunProgressMetrics(
            ResearchPlanCount: plans.Length,
            PlannedSearchQueryCount: plans.Sum(queries => queries.Length),
            LiteratureSearchCount: searches.Length,
            LiteratureSearchSourceCount: searches
                .Select(search => search.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            LiteratureSearchResultCount: searches.Sum(search => search.ResultCount),
            DiscoveryPathCount: discoveryPathCount,
            DistinctDiscoveredStudyCount: discoveredStudyIds.Length,
            CurrentSourceMaterialCount: sourceMaterials.Length,
            StructuredFullTextMaterialCount: sourceMaterials.Count(type => type == SourceMaterialType.StructuredFullText),
            AbstractMaterialCount: sourceMaterials.Count(type => type == SourceMaterialType.Abstract),
            EvidenceExtractionCount: extractions.Length,
            CompletedEvidenceExtractionCount: extractions.Count(status => status == EvidenceExtractionStatus.Completed),
            SkippedEvidenceExtractionCount: extractions.Count(status => status == EvidenceExtractionStatus.Skipped),
            EvidenceFindingCount: await _dbContext.Evidence
                .AsNoTracking()
                .CountAsync(evidence => evidence.ResearchRunId == researchRunId, cancellationToken),
            EvidenceEvaluationCount: evaluations.Length,
            CompletedEvidenceEvaluationCount: evaluations.Count(status => status == EvidenceEvaluationStatus.Completed),
            SkippedEvidenceEvaluationCount: evaluations.Count(status => status == EvidenceEvaluationStatus.Skipped),
            ResearchReportCount: reportIds.Length,
            ResearchReportClaimCount: claimCount);

        return new ResearchRunProgressSnapshot(
            run.Run.Id,
            run.Question,
            run.Run.Status,
            run.Run.CreatedAt,
            run.Run.StartedAt,
            run.Run.CompletedAt,
            run.Run.FailureReason,
            run.Run.ProcessingLeaseExpiresAt,
            run.Run.LastHeartbeatAt,
            run.Run.ProcessingLeaseVersion,
            metrics);
    }
}

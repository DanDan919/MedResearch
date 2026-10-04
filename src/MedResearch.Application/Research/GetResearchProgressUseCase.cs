using MedResearch.Domain;
using MedResearch.Application.Security;

namespace MedResearch.Application.Research;

public sealed class GetResearchProgressUseCase
{
    private static readonly ResearchRunStatus[] Timeline =
    [
        ResearchRunStatus.Queued,
        ResearchRunStatus.Planning,
        ResearchRunStatus.Searching,
        ResearchRunStatus.Extracting,
        ResearchRunStatus.Evaluating,
        ResearchRunStatus.Synthesizing,
        ResearchRunStatus.Completed
    ];

    private readonly IResearchProgressStore _progressStore;
    private readonly ICurrentActor _currentActor;

    public GetResearchProgressUseCase(IResearchProgressStore progressStore, ICurrentActor currentActor)
    {
        _progressStore = progressStore;
        _currentActor = currentActor;
    }

    public async Task<ResearchRunProgress?> ExecuteAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        var refreshedAt = DateTimeOffset.UtcNow;
        var snapshot = await _progressStore.FindResearchRunProgressSnapshotAsync(
            researchRunId,
            _currentActor.RequireSubjectId(),
            cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return new ResearchRunProgress(
            snapshot.ResearchRunId,
            snapshot.Question,
            snapshot.Status.ToString(),
            snapshot.CreatedAt,
            snapshot.StartedAt,
            snapshot.CompletedAt,
            snapshot.FailureReason,
            refreshedAt,
            new ResearchRunProcessingProgress(
                GetLeaseState(snapshot, refreshedAt),
                snapshot.ProcessingLeaseExpiresAt,
                snapshot.LastHeartbeatAt,
                snapshot.ProcessingLeaseVersion),
            snapshot.Metrics,
            BuildStages(snapshot));
    }

    private static string GetLeaseState(ResearchRunProgressSnapshot snapshot, DateTimeOffset refreshedAt)
    {
        if (snapshot.Status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled)
        {
            return "Terminal";
        }

        if (snapshot.ProcessingLeaseExpiresAt is null)
        {
            return "None";
        }

        return snapshot.ProcessingLeaseExpiresAt <= refreshedAt ? "Expired" : "Active";
    }

    private static IReadOnlyCollection<ResearchRunStageProgress> BuildStages(ResearchRunProgressSnapshot snapshot)
    {
        return Timeline
            .Select(stage => new ResearchRunStageProgress(
                stage.ToString(),
                GetStageState(snapshot, stage),
                GetStageMetrics(snapshot.Metrics, stage)))
            .ToArray();
    }

    private static string GetStageState(ResearchRunProgressSnapshot snapshot, ResearchRunStatus stage)
    {
        if (snapshot.Status == ResearchRunStatus.Completed)
        {
            return "Completed";
        }

        if (snapshot.Status is ResearchRunStatus.Failed or ResearchRunStatus.Cancelled)
        {
            return HasPersistedStageOutput(snapshot.Metrics, stage) ? "Completed" : "Pending";
        }

        if (stage == snapshot.Status)
        {
            return "Current";
        }

        var stageIndex = Array.IndexOf(Timeline, stage);
        var statusIndex = Array.IndexOf(Timeline, snapshot.Status);

        return stageIndex >= 0 && statusIndex >= 0 && stageIndex < statusIndex
            ? "Completed"
            : "Pending";
    }

    private static bool HasPersistedStageOutput(ResearchRunProgressMetrics metrics, ResearchRunStatus stage)
    {
        return stage switch
        {
            ResearchRunStatus.Queued => true,
            ResearchRunStatus.Planning => metrics.ResearchPlanCount > 0,
            ResearchRunStatus.Searching => metrics.LiteratureSearchCount > 0 || metrics.DiscoveryPathCount > 0,
            ResearchRunStatus.Extracting => metrics.CurrentSourceMaterialCount > 0 || metrics.EvidenceExtractionCount > 0,
            ResearchRunStatus.Evaluating => metrics.EvidenceEvaluationCount > 0,
            ResearchRunStatus.Synthesizing => metrics.ResearchReportCount > 0,
            ResearchRunStatus.Completed => snapshotTerminalCompleted(metrics),
            _ => false
        };

        static bool snapshotTerminalCompleted(ResearchRunProgressMetrics metrics)
        {
            return metrics.ResearchReportCount > 0;
        }
    }

    private static IReadOnlyCollection<ResearchRunProgressMetric> GetStageMetrics(
        ResearchRunProgressMetrics metrics,
        ResearchRunStatus stage)
    {
        return stage switch
        {
            ResearchRunStatus.Queued =>
            [
                new ResearchRunProgressMetric("Run records", 1)
            ],
            ResearchRunStatus.Planning =>
            [
                new ResearchRunProgressMetric("Plans", metrics.ResearchPlanCount),
                new ResearchRunProgressMetric("Search queries", metrics.PlannedSearchQueryCount)
            ],
            ResearchRunStatus.Searching =>
            [
                new ResearchRunProgressMetric("Searches", metrics.LiteratureSearchCount),
                new ResearchRunProgressMetric("Sources", metrics.LiteratureSearchSourceCount),
                new ResearchRunProgressMetric("Provider results", metrics.LiteratureSearchResultCount),
                new ResearchRunProgressMetric("Discovery paths", metrics.DiscoveryPathCount),
                new ResearchRunProgressMetric("Distinct studies", metrics.DistinctDiscoveredStudyCount)
            ],
            ResearchRunStatus.Extracting =>
            [
                new ResearchRunProgressMetric("Current source materials", metrics.CurrentSourceMaterialCount),
                new ResearchRunProgressMetric("Structured full text", metrics.StructuredFullTextMaterialCount),
                new ResearchRunProgressMetric("Abstract materials", metrics.AbstractMaterialCount),
                new ResearchRunProgressMetric("Extractions", metrics.EvidenceExtractionCount),
                new ResearchRunProgressMetric("Completed extractions", metrics.CompletedEvidenceExtractionCount),
                new ResearchRunProgressMetric("Skipped extractions", metrics.SkippedEvidenceExtractionCount),
                new ResearchRunProgressMetric("Evidence findings", metrics.EvidenceFindingCount)
            ],
            ResearchRunStatus.Evaluating =>
            [
                new ResearchRunProgressMetric("Evaluations", metrics.EvidenceEvaluationCount),
                new ResearchRunProgressMetric("Completed evaluations", metrics.CompletedEvidenceEvaluationCount),
                new ResearchRunProgressMetric("Skipped evaluations", metrics.SkippedEvidenceEvaluationCount)
            ],
            ResearchRunStatus.Synthesizing =>
            [
                new ResearchRunProgressMetric("Reports", metrics.ResearchReportCount),
                new ResearchRunProgressMetric("Claims", metrics.ResearchReportClaimCount)
            ],
            ResearchRunStatus.Completed =>
            [
                new ResearchRunProgressMetric("Reports", metrics.ResearchReportCount),
                new ResearchRunProgressMetric("Claims", metrics.ResearchReportClaimCount)
            ],
            _ => []
        };
    }
}

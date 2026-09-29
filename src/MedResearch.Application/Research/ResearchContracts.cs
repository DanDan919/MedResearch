using MedResearch.Domain;

namespace MedResearch.Application.Research;

public sealed record CreateResearchCommand(string? Question);

public sealed record CreateResearchResult(Guid ResearchRunId, string Status);

public sealed record ResearchRunDetails(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

public sealed record ListResearchRunsQuery(int? Page, int? PageSize, string? Status);

public sealed record ResearchRunSummary(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

public sealed record ResearchRunListResult(
    IReadOnlyCollection<ResearchRunSummary> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ResearchRunProgress(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    DateTimeOffset RefreshedAt,
    ResearchRunProcessingProgress Processing,
    ResearchRunProgressMetrics Metrics,
    IReadOnlyCollection<ResearchRunStageProgress> Stages);

public sealed record ResearchRunProcessingProgress(
    string LeaseState,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastHeartbeatAt,
    long LeaseVersion);

public sealed record ResearchRunProgressMetrics(
    int ResearchPlanCount,
    int PlannedSearchQueryCount,
    int LiteratureSearchCount,
    int LiteratureSearchSourceCount,
    int LiteratureSearchResultCount,
    int DiscoveryPathCount,
    int DistinctDiscoveredStudyCount,
    int CurrentSourceMaterialCount,
    int StructuredFullTextMaterialCount,
    int AbstractMaterialCount,
    int EvidenceExtractionCount,
    int CompletedEvidenceExtractionCount,
    int SkippedEvidenceExtractionCount,
    int EvidenceFindingCount,
    int EvidenceEvaluationCount,
    int CompletedEvidenceEvaluationCount,
    int SkippedEvidenceEvaluationCount,
    int ResearchReportCount,
    int ResearchReportClaimCount);

public sealed record ResearchRunStageProgress(
    string Stage,
    string State,
    IReadOnlyCollection<ResearchRunProgressMetric> Metrics);

public sealed record ResearchRunProgressMetric(string Label, int Value);

public sealed record ResearchRunProgressSnapshot(
    Guid ResearchRunId,
    string Question,
    ResearchRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    DateTimeOffset? ProcessingLeaseExpiresAt,
    DateTimeOffset? LastHeartbeatAt,
    long ProcessingLeaseVersion,
    ResearchRunProgressMetrics Metrics);

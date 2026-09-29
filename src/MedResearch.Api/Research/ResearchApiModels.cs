namespace MedResearch.Api.Research;

public sealed record CreateResearchRequest(string? Question);

public sealed record CreateResearchResponse(Guid ResearchRunId, string Status);

public sealed record ResearchRunResponse(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

public sealed record ResearchRunSummaryResponse(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

public sealed record ResearchRunListResponse(
    IReadOnlyCollection<ResearchRunSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ResearchRunProgressResponse(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    DateTimeOffset RefreshedAt,
    ResearchRunProcessingProgressResponse Processing,
    ResearchRunProgressMetricsResponse Metrics,
    IReadOnlyCollection<ResearchRunStageProgressResponse> Stages);

public sealed record ResearchRunProcessingProgressResponse(
    string LeaseState,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastHeartbeatAt,
    long LeaseVersion);

public sealed record ResearchRunProgressMetricsResponse(
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

public sealed record ResearchRunStageProgressResponse(
    string Stage,
    string State,
    IReadOnlyCollection<ResearchRunProgressMetricResponse> Metrics);

public sealed record ResearchRunProgressMetricResponse(string Label, int Value);

public sealed record ResearchReportResponse(
    Guid ResearchRunId,
    Guid ResearchReportId,
    string Status,
    string? InsufficientEvidenceReason,
    string Question,
    string ExecutiveSummary,
    string EvidenceSummary,
    string ConflictSummary,
    string LimitationsSummary,
    string Conclusion,
    string SynthesisConfidence,
    string PromptVersion,
    DateTimeOffset GeneratedAt,
    ResearchReportCoverageResponse Coverage,
    IReadOnlyCollection<string> DeterministicLimitations,
    IReadOnlyCollection<ResearchReportClaimResponse> Claims);

public sealed record ResearchReportCoverageResponse(
    int DiscoveredStudyCount,
    int ExtractedStudyCount,
    int EvaluatedStudyCount,
    int EvidenceFindingCount,
    int IncludedStudyCount,
    int IncludedEvidenceFindingCount,
    int SearchQueryCount,
    int StudiesWithNoExtractableEvidence,
    int StudiesWithInsufficientEvaluationSource,
    bool PotentialConflictDetected,
    bool EvidenceTruncated,
    bool UsesAbstractLevelEvidenceOnly,
    IReadOnlyCollection<string> SearchedSources);

public sealed record ResearchReportClaimResponse(
    Guid ClaimId,
    string ClaimType,
    string Direction,
    string Text,
    int Ordinal,
    IReadOnlyCollection<ResearchReportCitationResponse> Citations);

public sealed record ResearchReportCitationResponse(
    Guid EvidenceId,
    Guid StudyId,
    string? Pmid,
    string? Pmcid,
    string? Doi,
    string Title,
    string SupportingText,
    string EvidenceDirection,
    int Ordinal);

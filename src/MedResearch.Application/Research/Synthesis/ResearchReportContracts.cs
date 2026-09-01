using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public sealed record AcceptedResearchReportClaim(
    ResearchReportClaimType ClaimType,
    ResearchReportClaimDirection Direction,
    string Text,
    IReadOnlyCollection<Guid> EvidenceIds,
    int Ordinal);

public sealed record ResearchSynthesisResult(
    Guid ResearchRunId,
    ResearchReportStatus Status,
    ResearchReportInsufficientEvidenceReason? InsufficientEvidenceReason,
    string ExecutiveSummary,
    string EvidenceSummary,
    string ConflictSummary,
    string LimitationsSummary,
    string Conclusion,
    SynthesisConfidence SynthesisConfidence,
    string? SynthesizerProvider,
    string? SynthesizerModel,
    string PromptVersion,
    DateTimeOffset GeneratedAt,
    SynthesisCorpusStatistics Statistics,
    SynthesisSourceCoverage SourceCoverage,
    IReadOnlyCollection<string> DeterministicLimitations,
    IReadOnlyCollection<AcceptedResearchReportClaim> Claims);

public sealed record ResearchReportReadModel(
    Guid ResearchRunId,
    Guid ResearchReportId,
    ResearchReportStatus Status,
    ResearchReportInsufficientEvidenceReason? InsufficientEvidenceReason,
    string Question,
    string ExecutiveSummary,
    string EvidenceSummary,
    string ConflictSummary,
    string LimitationsSummary,
    string Conclusion,
    SynthesisConfidence SynthesisConfidence,
    string PromptVersion,
    DateTimeOffset GeneratedAt,
    ResearchReportCoverageReadModel Coverage,
    IReadOnlyCollection<string> DeterministicLimitations,
    IReadOnlyCollection<ResearchReportClaimReadModel> Claims);

public sealed record ResearchReportCoverageReadModel(
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

public sealed record ResearchReportClaimReadModel(
    Guid ClaimId,
    ResearchReportClaimType ClaimType,
    ResearchReportClaimDirection Direction,
    string Text,
    int Ordinal,
    IReadOnlyCollection<ResearchReportCitationReadModel> Citations);

public sealed record ResearchReportCitationReadModel(
    Guid EvidenceId,
    Guid StudyId,
    string? Pmid,
    string? Doi,
    string Title,
    string SupportingText,
    EvidenceDirection EvidenceDirection,
    int Ordinal);
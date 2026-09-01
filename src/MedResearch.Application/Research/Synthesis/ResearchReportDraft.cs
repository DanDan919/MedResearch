namespace MedResearch.Application.Research.Synthesis;

public sealed record ResearchReportDraft(
    string? ReportStatus,
    string? InsufficientEvidenceReason,
    string? ExecutiveSummary,
    string? EvidenceSummary,
    string? ConflictSummary,
    string? LimitationsSummary,
    string? Conclusion,
    string? SynthesisConfidence,
    IReadOnlyCollection<ResearchReportClaimDraft>? Claims);

public sealed record ResearchReportClaimDraft(
    string? Type,
    string? Direction,
    string? Text,
    IReadOnlyCollection<string>? EvidenceIds,
    string? Pmid = null,
    string? Doi = null,
    string? StudyId = null);
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public sealed record AcceptedResearchReportClaim(
    ResearchReportClaimType ClaimType,
    ResearchReportClaimDirection Direction,
    string Text,
    IReadOnlyCollection<Guid> EvidenceIds,
    int Ordinal,
    ResearchClaimSemantics? Semantics = null);

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
    int StructuredFullTextStudyCount,
    int AbstractOnlyStudyCount,
    int NoSourceMaterialStudyCount,
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
    IReadOnlyCollection<ResearchReportCitationReadModel> Citations,
    ResearchClaimGroundingStatus GroundingStatus = ResearchClaimGroundingStatus.LegacyUnverified,
    ResearchClaimSemantics? Semantics = null);

public sealed record ResearchReportCitationReadModel(
    Guid EvidenceId,
    Guid StudyId,
    string? Pmid,
    string? Pmcid,
    string? Doi,
    string Title,
    string? Journal,
    int? PublicationYear,
    int? PublicationMonth,
    int? PublicationDay,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string StudySource,
    string Outcome,
    string ResultSummary,
    string SupportingText,
    EvidenceDirection EvidenceDirection,
    EvidenceSourceScope SourceScope,
    bool GroundingValidated,
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    string? StudyDesign,
    int? SampleSize,
    string? EffectMeasure,
    decimal? EffectValue,
    decimal? ConfidenceIntervalLower,
    decimal? ConfidenceIntervalUpper,
    decimal? ConfidenceLevel,
    decimal? ReportedStandardError,
    decimal? PValue,
    DateTimeOffset ExtractedAt,
    ResearchReportSourceMaterialReadModel? SourceMaterial,
    int Ordinal);

public sealed record ResearchReportSourceMaterialReadModel(
    Guid SourceMaterialId,
    string Type,
    string Provider,
    string RetrievalMethod,
    int ContentVersion,
    DateTimeOffset RetrievedAt,
    string AccessStatus,
    bool WasTruncated,
    IReadOnlyCollection<string> SectionNames);

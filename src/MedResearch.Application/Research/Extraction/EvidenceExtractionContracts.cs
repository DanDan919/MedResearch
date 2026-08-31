using MedResearch.Domain;

namespace MedResearch.Application.Research.Extraction;

public sealed record EvidenceExtractionPlanContext(
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    IReadOnlyCollection<string> Outcomes,
    IReadOnlyCollection<string> PreferredStudyTypes,
    IReadOnlyCollection<string> ExclusionHints);

public sealed record EvidenceExtractionStudyContext(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    EvidenceExtractionPlanContext? Plan,
    Guid StudyId,
    string Title,
    string? Abstract,
    string? Pmid,
    string? Doi,
    string? Journal,
    DateOnly? PublicationDate,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string Source);

public sealed record EvidenceExtractionWorkItemSet(
    int TotalDiscoveredStudyCount,
    IReadOnlyCollection<EvidenceExtractionStudyContext> Studies);

public sealed record AcceptedEvidenceFinding(
    string Outcome,
    string ResultSummary,
    string SupportingText,
    EvidenceDirection Direction,
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    string? StudyDesign,
    int? SampleSize,
    string? EffectMeasure,
    decimal? EffectValue,
    decimal? ConfidenceIntervalLower,
    decimal? ConfidenceIntervalUpper,
    decimal? PValue);

public sealed record EvidenceExtractionResult(
    Guid ResearchRunId,
    Guid StudyId,
    EvidenceExtractionStatus Status,
    EvidenceExtractionSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    string? Provider,
    string? Model,
    string PromptVersion,
    DateTimeOffset ExtractedAt,
    bool GroundingValidated,
    IReadOnlyCollection<AcceptedEvidenceFinding> Findings);

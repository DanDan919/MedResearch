using MedResearch.Domain;

namespace MedResearch.Application.Research.Evaluation;

public sealed record EvaluationPlanContext(
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    IReadOnlyCollection<string> Outcomes,
    IReadOnlyCollection<string> PreferredStudyTypes,
    IReadOnlyCollection<string> ExclusionHints);

public sealed record EvaluationStudyContext(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    EvaluationPlanContext? Plan,
    Guid StudyId,
    string Title,
    string? Abstract,
    string? Pmid,
    string? Doi,
    string? Journal,
    DateOnly? PublicationDate,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string Source,
    EvidenceExtractionStatus ExtractionStatus,
    EvidenceExtractionSkipReason? ExtractionSkipReason,
    EvidenceSourceScope SourceScope,
    string ExtractionPromptVersion,
    IReadOnlyCollection<EvaluationEvidenceContext> Evidence);

public sealed record EvaluationEvidenceContext(
    Guid EvidenceId,
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
    decimal? PValue,
    bool GroundingValidated);

public sealed record EvidenceEvaluationWorkItemSet(
    int TotalExtractionCount,
    IReadOnlyCollection<EvaluationStudyContext> Studies);

public sealed record EvidenceEvaluationSignalSet(
    EvidenceSourceScope SourceScope,
    int EvidenceCount,
    bool HasSampleSize,
    bool HasEffectEstimate,
    bool HasConfidenceInterval,
    bool HasPValue,
    bool HasComparator,
    StudyDesignClassification MetadataStudyDesignHint,
    IReadOnlyCollection<string> ReportingLimitations);

public sealed record EvidenceEvaluationResult(
    Guid ResearchRunId,
    Guid StudyId,
    IReadOnlyCollection<Guid> EvidenceIds,
    EvidenceEvaluationStatus Status,
    EvidenceEvaluationSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    string? EvaluatorProvider,
    string? EvaluatorModel,
    string PromptVersion,
    DateTimeOffset EvaluatedAt,
    StudyDesignClassification StudyDesign,
    MethodologicalAssessmentState SampleInformation,
    ComparatorPresence ComparatorPresence,
    string? ComparatorDescription,
    MethodologicalAssessmentState Randomization,
    MethodologicalAssessmentState Blinding,
    MethodologicalAssessmentState AllocationConcealment,
    MethodologicalAssessmentState AttritionMissingData,
    MethodologicalAssessmentState Precision,
    DirectnessRating Directness,
    MethodologicalConfidence OverallConfidence,
    string Rationale,
    IReadOnlyCollection<string> ReportingLimitations,
    IReadOnlyCollection<string> AuthorReportedLimitations,
    EvidenceEvaluationSignalSet Signals,
    int UnknownDomainCount,
    int InsufficientSourceDomainCount);

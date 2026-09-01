using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public enum SynthesisConflictStatus
{
    NotDetected = 0,
    Present = 1,
    Unknown = 2
}

public sealed record SynthesisPlanContext(
    Guid PlanId,
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    IReadOnlyCollection<string> Outcomes,
    IReadOnlyCollection<string> PreferredStudyTypes,
    IReadOnlyCollection<string> SearchQueries,
    IReadOnlyCollection<string> ExclusionHints);

public sealed record SynthesisCorpusStatistics(
    int DiscoveredStudyCount,
    int ExtractedStudyCount,
    int EvaluatedStudyCount,
    int EvidenceFindingCount,
    int IncludedStudyCount,
    int IncludedEvidenceFindingCount,
    int SearchQueryCount,
    int StudiesWithNoExtractableEvidence,
    int StudiesWithInsufficientEvaluationSource);

public sealed record SynthesisSourceCoverage(
    IReadOnlyCollection<string> SearchedSources,
    bool UsesAbstractLevelEvidenceOnly,
    bool IncludesFullTextEvidence,
    bool EvidenceTruncated,
    bool PotentialConflictDetected,
    int ExecutedSearchCount);

public sealed record SynthesisOutcomeDirectionSummary(
    string Outcome,
    int PositiveCount,
    int NegativeCount,
    int NoClearEffectCount,
    int MixedCount,
    int NotReportedCount,
    SynthesisConflictStatus ConflictStatus);

public sealed record SynthesisStudyContext(
    Guid StudyId,
    string Title,
    string? Pmid,
    string? Doi,
    string? Journal,
    DateOnly? PublicationDate,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string Source,
    SynthesisEvaluationContext? Evaluation,
    IReadOnlyCollection<SynthesisEvidenceContext> Evidence);

public sealed record SynthesisEvidenceContext(
    Guid EvidenceId,
    Guid ResearchRunId,
    Guid StudyId,
    string Outcome,
    string ResultSummary,
    string SupportingText,
    EvidenceDirection Direction,
    EvidenceSourceScope SourceScope,
    DateTimeOffset ExtractedAt,
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

public sealed record SynthesisEvaluationContext(
    Guid EvaluationId,
    Guid ResearchRunId,
    Guid StudyId,
    EvidenceEvaluationStatus Status,
    EvidenceEvaluationSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    StudyDesignClassification StudyDesign,
    MethodologicalAssessmentState SampleInformation,
    ComparatorPresence ComparatorPresence,
    MethodologicalAssessmentState Randomization,
    MethodologicalAssessmentState Blinding,
    MethodologicalAssessmentState AllocationConcealment,
    MethodologicalAssessmentState AttritionMissingData,
    MethodologicalAssessmentState Precision,
    DirectnessRating Directness,
    MethodologicalConfidence OverallConfidence,
    IReadOnlyCollection<Guid> EvidenceIds,
    IReadOnlyCollection<string> ReportingLimitations,
    int UnknownDomainCount,
    int InsufficientSourceDomainCount);

public sealed record SynthesisContext(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    SynthesisPlanContext? Plan,
    SynthesisCorpusStatistics Statistics,
    SynthesisSourceCoverage SourceCoverage,
    IReadOnlyCollection<SynthesisStudyContext> Studies,
    IReadOnlyCollection<SynthesisOutcomeDirectionSummary> OutcomeDirectionSummaries,
    IReadOnlyCollection<string> DeterministicLimitations);

public sealed record SynthesisCorpusSnapshot(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    SynthesisPlanContext? Plan,
    IReadOnlyCollection<SynthesisStudySnapshot> Studies,
    IReadOnlyCollection<SynthesisEvidenceContext> Evidence,
    IReadOnlyCollection<SynthesisEvaluationContext> Evaluations,
    IReadOnlyCollection<SynthesisSearchSnapshot> Searches,
    IReadOnlyCollection<SynthesisExtractionSnapshot> Extractions);

public sealed record SynthesisStudySnapshot(
    Guid StudyId,
    string Title,
    string? Pmid,
    string? Doi,
    string? Journal,
    DateOnly? PublicationDate,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string Source,
    DateTimeOffset DiscoveredAt);

public sealed record SynthesisSearchSnapshot(
    Guid LiteratureSearchId,
    Guid ResearchRunId,
    string Source,
    string Query,
    DateTimeOffset SearchedAt,
    int ResultCount,
    int PersistedStudyCount,
    int DuplicateStudyCount);

public sealed record SynthesisExtractionSnapshot(
    Guid ExtractionId,
    Guid ResearchRunId,
    Guid StudyId,
    EvidenceExtractionStatus Status,
    EvidenceExtractionSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    int EvidenceCount,
    bool GroundingValidated);
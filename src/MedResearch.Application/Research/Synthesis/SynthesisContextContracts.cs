using MedResearch.Application.Research.Quantitative;
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
    int StudiesWithInsufficientEvaluationSource,
    int StructuredFullTextStudyCount,
    int AbstractOnlyStudyCount,
    int NoSourceMaterialStudyCount);

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
    string? Pmcid,
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
    Guid EvidenceExtractionId,
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
    decimal? PValue,
    decimal? ConfidenceLevel = null,
    decimal? ReportedStandardError = null);

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

public sealed record SynthesisQuantitativeContributionContext(
    Guid EvidenceId,
    Guid StudyId,
    double AnalysisScaleEffect,
    double AnalysisScaleVariance,
    double Weight,
    double NormalizedWeight);

public sealed record SynthesisQuantitativeHeterogeneityDiagnosticsContext(
    double CochransQ,
    int DegreesOfFreedom,
    double ISquared,
    int StudyCount,
    string AlgorithmVersion);

public sealed record SynthesisBetweenStudyVarianceContext(
    double? TauSquared,
    BetweenStudyVarianceEstimator Estimator,
    BetweenStudyVarianceEstimateStatus Status,
    string AlgorithmVersion,
    int StudyCount,
    bool Converged,
    int IterationCount,
    BetweenStudyVarianceFailureReason? FailureReason);

public sealed record SynthesisHksjInferenceContext(
    QuantitativeSynthesisStatus Status,
    QuantitativeConfidenceIntervalMethod ConfidenceIntervalMethod,
    string AlgorithmVersion,
    decimal OutputConfidenceLevel,
    int StudyCount,
    int? DegreesOfFreedom,
    double? VarianceAdjustment,
    double? CriticalValue,
    double? AnalysisScaleEffect,
    double? AnalysisScaleVariance,
    double? AnalysisScaleStandardError,
    double? AnalysisScaleConfidenceIntervalLower,
    double? AnalysisScaleConfidenceIntervalUpper,
    double? ReportedScaleEffect,
    double? ReportedScaleConfidenceIntervalLower,
    double? ReportedScaleConfidenceIntervalUpper,
    IReadOnlyCollection<QuantitativeHksjFailureReason> FailureReasons);

public sealed record SynthesisRandomEffectsResultContext(
    QuantitativeSynthesisStatus Status,
    QuantitativeSynthesisMethod Method,
    string AlgorithmVersion,
    QuantitativeConfidenceIntervalMethod ConfidenceIntervalMethod,
    decimal OutputConfidenceLevel,
    double? TauSquared,
    BetweenStudyVarianceEstimator TauSquaredEstimator,
    string TauSquaredAlgorithmVersion,
    int StudyCount,
    double? AnalysisScaleEffect,
    double? AnalysisScaleVariance,
    double? AnalysisScaleStandardError,
    double? AnalysisScaleConfidenceIntervalLower,
    double? AnalysisScaleConfidenceIntervalUpper,
    double? ReportedScaleEffect,
    double? ReportedScaleConfidenceIntervalLower,
    double? ReportedScaleConfidenceIntervalUpper,
    SynthesisHksjInferenceContext? HksjInference,
    IReadOnlyCollection<SynthesisQuantitativeContributionContext> Contributions,
    IReadOnlyCollection<QuantitativeRandomEffectsFailureReason> FailureReasons);
public sealed record SynthesisQuantitativeResultContext(
    string GroupKey,
    string OutcomeGroupKey,
    string PopulationCompatibilityKey,
    string ComparatorCompatibilityKey,
    string StudyDesignCompatibilityKey,
    EffectMeasureType EffectMeasureType,
    QuantitativeSynthesisMethod Method,
    string AlgorithmVersion,
    decimal OutputConfidenceLevel,
    double AnalysisScaleEffect,
    double AnalysisScaleStandardError,
    double AnalysisScaleConfidenceIntervalLower,
    double AnalysisScaleConfidenceIntervalUpper,
    double ReportedScaleEffect,
    double ReportedScaleConfidenceIntervalLower,
    double ReportedScaleConfidenceIntervalUpper,
    int EvidenceCount,
    int UniqueStudyCount,
    SynthesisQuantitativeHeterogeneityDiagnosticsContext? HeterogeneityDiagnostics,
    SynthesisBetweenStudyVarianceContext? BetweenStudyVariance,
    SynthesisRandomEffectsResultContext? RandomEffects,
    IReadOnlyCollection<SynthesisQuantitativeContributionContext> Contributions);
public sealed record SynthesisContext(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    SynthesisPlanContext? Plan,
    SynthesisCorpusStatistics Statistics,
    SynthesisSourceCoverage SourceCoverage,
    IReadOnlyCollection<SynthesisStudyContext> Studies,
    IReadOnlyCollection<SynthesisOutcomeDirectionSummary> OutcomeDirectionSummaries,
    IReadOnlyCollection<string> DeterministicLimitations,
    IReadOnlyCollection<SynthesisQuantitativeResultContext> QuantitativeSyntheses)
{
    public SynthesisContext(
        Guid researchRunId,
        Guid researchQuestionId,
        string researchQuestion,
        SynthesisPlanContext? plan,
        SynthesisCorpusStatistics statistics,
        SynthesisSourceCoverage sourceCoverage,
        IReadOnlyCollection<SynthesisStudyContext> studies,
        IReadOnlyCollection<SynthesisOutcomeDirectionSummary> outcomeDirectionSummaries,
        IReadOnlyCollection<string> deterministicLimitations)
        : this(
            researchRunId,
            researchQuestionId,
            researchQuestion,
            plan,
            statistics,
            sourceCoverage,
            studies,
            outcomeDirectionSummaries,
            deterministicLimitations,
            [])
    {
    }
}

public sealed record SynthesisCorpusSnapshot(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    string ResearchQuestion,
    SynthesisPlanContext? Plan,
    IReadOnlyCollection<SynthesisStudySnapshot> Studies,
    IReadOnlyCollection<SynthesisEvidenceContext> Evidence,
    IReadOnlyCollection<SynthesisEvaluationContext> Evaluations,
    IReadOnlyCollection<SynthesisSearchSnapshot> Searches,
    IReadOnlyCollection<SynthesisExtractionSnapshot> Extractions,
    IReadOnlyCollection<SynthesisSourceMaterialSnapshot> SourceMaterials);

public sealed record SynthesisStudySnapshot(
    Guid StudyId,
    string Title,
    string? Pmid,
    string? Pmcid,
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
    Guid? SourceMaterialId,
    int EvidenceCount,
    bool GroundingValidated);

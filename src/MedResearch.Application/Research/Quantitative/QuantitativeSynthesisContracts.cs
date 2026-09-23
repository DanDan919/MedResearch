namespace MedResearch.Application.Research.Quantitative;

public enum QuantitativeSynthesisStatus
{
    NotSynthesizable = 0,
    Synthesized = 1
}

public enum QuantitativeSynthesisMethod
{
    FixedEffectInverseVariance = 0,
    RandomEffectsInverseVariance = 1
}

public enum QuantitativeConfidenceIntervalMethod
{
    WaldStandardNormal = 0
}

public enum BetweenStudyVarianceEstimator
{
    RestrictedMaximumLikelihood = 0
}

public enum BetweenStudyVarianceEstimateStatus
{
    Estimated = 0,
    NotEstimated = 1
}

public enum BetweenStudyVarianceFailureReason
{
    None = 0,
    InsufficientIndependentStudies = 1,
    InvalidInput = 2,
    NonFiniteCalculation = 3,
    FailedToBracket = 4,
    MaxIterationsExceeded = 5
}

public enum QuantitativeRandomEffectsFailureReason
{
    BetweenStudyVarianceNotEstimated = 0,
    InvalidTauSquared = 1,
    InvalidVariance = 2,
    InvalidEffect = 3,
    InvalidWeight = 4,
    NonFinitePooledEffect = 5,
    NonFiniteConfidenceInterval = 6,
    BackTransformationFailed = 7,
    DuplicateContribution = 8
}

public enum QuantitativeSynthesisRejectionReason
{
    GroupNotReadyForMetaAnalysisInput = 0,
    InsufficientIndependentStudies = 1,
    DependentEvidenceFromSameStudy = 2,
    UnsupportedEffectMeasure = 3,
    MissingEligibleEvidence = 4,
    MissingNormalizedEffect = 5,
    MissingVariance = 6,
    InvalidVariance = 7,
    InvalidWeight = 8,
    NonFinitePooledEffect = 9,
    NonFiniteConfidenceInterval = 10,
    BackTransformationFailed = 11,
    DuplicateEvidenceContribution = 12,
    NonFiniteHeterogeneityDiagnostics = 13
}

public sealed record QuantitativeSynthesisOptions
{
    public const string SectionName = "QuantitativeSynthesis";

    public decimal OutputConfidenceLevel { get; init; } = 0.95m;

    public int MinimumUniqueStudies { get; init; } = 2;

    public double BoundedOutputConfidenceLevel => (double)OutputConfidenceLevel;

    public void Validate()
    {
        if (OutputConfidenceLevel <= 0 || OutputConfidenceLevel >= 1)
        {
            throw new InvalidOperationException("QuantitativeSynthesis:OutputConfidenceLevel must be greater than 0 and less than 1.");
        }

        if (MinimumUniqueStudies < 2)
        {
            throw new InvalidOperationException("QuantitativeSynthesis:MinimumUniqueStudies must be at least 2.");
        }
    }
}

public sealed record QuantitativeSynthesisReadiness(
    Guid ResearchRunId,
    IReadOnlyCollection<QuantitativeSynthesisResult> Results,
    int SynthesizedGroupCount,
    int NotSynthesizableGroupCount,
    string AlgorithmVersion);

public sealed record QuantitativeSynthesisResult(
    Guid ResearchRunId,
    string GroupKey,
    string OutcomeGroupKey,
    string PopulationCompatibilityKey,
    string ComparatorCompatibilityKey,
    string StudyDesignCompatibilityKey,
    EffectMeasureType EffectMeasureType,
    QuantitativeSynthesisStatus Status,
    QuantitativeSynthesisMethod Method,
    string AlgorithmVersion,
    decimal OutputConfidenceLevel,
    int EvidenceCount,
    int UniqueStudyCount,
    double? AnalysisScaleEffect,
    double? AnalysisScaleVariance,
    double? AnalysisScaleStandardError,
    double? AnalysisScaleConfidenceIntervalLower,
    double? AnalysisScaleConfidenceIntervalUpper,
    double? ReportedScaleEffect,
    double? ReportedScaleConfidenceIntervalLower,
    double? ReportedScaleConfidenceIntervalUpper,
    QuantitativeHeterogeneityDiagnostics? HeterogeneityDiagnostics,
    BetweenStudyVarianceEstimate? BetweenStudyVariance,
    QuantitativeRandomEffectsSynthesisResult? RandomEffects,
    IReadOnlyCollection<QuantitativeSynthesisContribution> Contributions,
    IReadOnlyCollection<QuantitativeSynthesisRejectionReason> RejectionReasons);

public sealed record QuantitativeRandomEffectsSynthesisResult(
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
    IReadOnlyCollection<QuantitativeSynthesisContribution> Contributions,
    IReadOnlyCollection<QuantitativeRandomEffectsFailureReason> FailureReasons);

public sealed record QuantitativeHeterogeneityDiagnostics(
    double CochransQ,
    int DegreesOfFreedom,
    double ISquared,
    int StudyCount,
    string AlgorithmVersion);

public sealed record BetweenStudyVarianceEstimate(
    double? TauSquared,
    BetweenStudyVarianceEstimator Estimator,
    BetweenStudyVarianceEstimateStatus Status,
    string AlgorithmVersion,
    int StudyCount,
    bool Converged,
    int IterationCount,
    BetweenStudyVarianceFailureReason? FailureReason);

public sealed record QuantitativeSynthesisContribution(
    Guid EvidenceId,
    Guid StudyId,
    Guid EvidenceExtractionId,
    Guid SourceMaterialId,
    double AnalysisScaleEffect,
    double AnalysisScaleVariance,
    double AnalysisScaleStandardError,
    double Weight,
    double NormalizedWeight);

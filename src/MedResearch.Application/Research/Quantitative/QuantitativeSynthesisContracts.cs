namespace MedResearch.Application.Research.Quantitative;

public enum QuantitativeSynthesisStatus
{
    NotSynthesizable = 0,
    Synthesized = 1
}

public enum QuantitativeSynthesisMethod
{
    FixedEffectInverseVariance = 0
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
    DuplicateEvidenceContribution = 12
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
    IReadOnlyCollection<QuantitativeSynthesisContribution> Contributions,
    IReadOnlyCollection<QuantitativeSynthesisRejectionReason> RejectionReasons);

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

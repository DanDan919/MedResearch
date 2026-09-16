namespace MedResearch.Application.Research.Quantitative;

public enum EffectMeasureType
{
    Unknown = 0,
    OddsRatio = 1,
    RiskRatio = 2,
    HazardRatio = 3,
    RiskDifference = 4,
    MeanDifference = 5,
    StandardizedMeanDifference = 6,
    Correlation = 7,
    RegressionCoefficient = 8,
    Proportion = 9,
    Other = 10
}

public enum QuantitativeEligibility
{
    Unknown = 0,
    Eligible = 1,
    Ineligible = 2
}

public enum QuantitativeIneligibilityReason
{
    MissingEffectMeasure = 0,
    UnknownEffectMeasure = 1,
    UnsupportedEffectMeasure = 2,
    MissingEffectValue = 3,
    InvalidNumericValue = 4,
    MissingConfidenceLevel = 5,
    MissingUncertainty = 6,
    MissingSampleSize = 7,
    OutcomeNotCompatible = 8,
    PopulationNotCompatible = 9,
    ComparatorNotCompatible = 10,
    StudyDesignNotCompatible = 11,
    EffectMeasureNotCompatible = 12,
    InsufficientStatisticalData = 13,
    SourceTruncated = 14,
    AmbiguousDirection = 15,
    DuplicateStudyContribution = 16
}

public enum StatisticOrigin
{
    Reported = 0,
    DerivedFromConfidenceInterval = 1,
    DerivedFromStandardError = 2,
    DerivedFromSampleSize = 3
}

public sealed record QuantitativeEvidenceReadiness(
    Guid ResearchRunId,
    IReadOnlyCollection<QuantitativeEvidenceAssessment> Assessments,
    IReadOnlyCollection<CompatibleEvidenceGroup> CompatibleGroups,
    int EligibleEvidenceCount,
    int IneligibleEvidenceCount,
    string AlgorithmVersion);

public sealed record QuantitativeEvidenceAssessment(
    Guid EvidenceId,
    Guid ResearchRunId,
    Guid StudyId,
    Guid EvidenceExtractionId,
    Guid SourceMaterialId,
    string OutcomeGroupKey,
    string? PopulationCompatibilityKey,
    string? ComparatorCompatibilityKey,
    string? StudyDesignCompatibilityKey,
    string? ReportedEffectMeasure,
    EffectMeasureType EffectMeasureType,
    QuantitativeEligibility Eligibility,
    decimal? ReportedEffectValue,
    double? NormalizedEffect,
    double? StandardError,
    double? Variance,
    StatisticOrigin? NormalizedEffectOrigin,
    StatisticOrigin? StandardErrorOrigin,
    bool SourceWasTruncated,
    IReadOnlyCollection<QuantitativeIneligibilityReason> ReasonCodes);

public sealed record CompatibleEvidenceGroup(
    string GroupKey,
    string OutcomeGroupKey,
    string PopulationCompatibilityKey,
    string ComparatorCompatibilityKey,
    string StudyDesignCompatibilityKey,
    EffectMeasureType EffectMeasureType,
    IReadOnlyCollection<Guid> EvidenceIds,
    IReadOnlyCollection<Guid> StudyIds,
    int EvidenceCount,
    int UniqueStudyCount,
    bool HasDependentEvidenceFromSameStudy,
    bool ReadyForFutureMetaAnalysisInput);
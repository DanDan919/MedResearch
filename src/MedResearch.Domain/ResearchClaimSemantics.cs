namespace MedResearch.Domain;

public enum ResearchClaimKind { QualitativeEffect, MixedEvidence, ReportedStudyResult, QuantitativeSynthesis, InsufficientEvidence }
public enum ResearchClaimGroundingStatus { LegacyUnverified, StructuredValidated }
public enum ResearchClaimStatistic
{
    StudyEffect, StudyConfidenceInterval, StudyStandardError, StudyPValue, StudySampleSize,
    FixedEffectWald, RandomEffectsWald, RandomEffectsHksj, RandomEffectsPredictionInterval,
    CochransQ, ISquared, TauSquared
}

// Values retain their original decimal (reported) or double (calculated) representation.
public sealed record ResearchClaimNumericSnapshot(
    string Label, decimal? StudyValue, double? ArtifactValue, decimal? StudyLower, decimal? StudyUpper,
    double? ArtifactLower, double? ArtifactUpper, decimal? ConfidenceLevel, string? Operator,
    int? DegreesOfFreedom, string? AlgorithmVersion);

public sealed record ResearchClaimSemantics(
    string ProtocolVersion, ResearchClaimKind Kind, string? Outcome, string? Population,
    string? ExposureOrIntervention, string? Comparator, string? Timepoint,
    ResearchReportClaimDirection Direction, IReadOnlyCollection<Guid> EvidenceIds,
    Guid? NumericEvidenceId, Guid? QuantitativeArtifactId, string? GroupKey, string? SnapshotFingerprint,
    ResearchClaimStatistic? Statistic, ResearchClaimNumericSnapshot? Numeric);

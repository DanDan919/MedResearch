using MedResearch.Domain;

namespace MedResearch.Application.Research.Provenance;

public sealed record ResearchProvenanceReadModel(
    Guid ResearchRunId,
    string Question,
    ResearchRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    ResearchProvenanceCoverage Coverage,
    IReadOnlyCollection<ResearchPlanProvenance> Plans,
    IReadOnlyCollection<LiteratureSearchProvenance> Searches,
    IReadOnlyCollection<StudyProvenance> Studies,
    IReadOnlyCollection<ResearchReportClaimProvenance> ReportClaims,
    IReadOnlyCollection<QuantitativeContributionProvenance> QuantitativeContributions,
    IReadOnlyCollection<LiteratureProviderAttemptProvenance> ProviderAttempts);

public sealed record LiteratureProviderAttemptProvenance(
    Guid AttemptId, Guid ResearchPlanId, string Source, string Query,
    LiteratureProviderAttemptStatus Status, int? ResultCount, LiteratureProviderFailureCategory? FailureCategory,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, Guid? LiteratureSearchId);

public sealed record ResearchProvenanceCoverage(
    int ResearchPlanCount,
    int LiteratureSearchCount,
    int DiscoveryPathCount,
    int DistinctStudyCount,
    int SourceMaterialCount,
    int EvidenceExtractionCount,
    int EvidenceFindingCount,
    int EvidenceEvaluationCount,
    int ResearchReportClaimCount,
    bool HasPersistedProviderFailureProvenance);

public sealed record ResearchPlanProvenance(
    Guid ResearchPlanId,
    string OriginalQuestion,
    IReadOnlyCollection<string> SearchQueries,
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset GeneratedAt);

public sealed record LiteratureSearchProvenance(
    Guid LiteratureSearchId,
    Guid? ResearchPlanId,
    string Source,
    string Query,
    DateTimeOffset SearchedAt,
    int ResultCount,
    int PersistedStudyCount,
    int DuplicateStudyCount,
    string ResultStatus);

public sealed record StudyProvenance(
    Guid StudyId,
    string Title,
    string? Pmid,
    string? Pmcid,
    string? Doi,
    string? Journal,
    int? PublicationYear,
    int? PublicationMonth,
    int? PublicationDay,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string Source,
    IReadOnlyCollection<StudyDiscoveryProvenance> DiscoveryPaths,
    IReadOnlyCollection<SourceMaterialProvenance> SourceMaterials,
    IReadOnlyCollection<EvidenceExtractionProvenance> Extractions,
    IReadOnlyCollection<EvidenceProvenance> Evidence,
    IReadOnlyCollection<EvidenceEvaluationProvenance> Evaluations);

public sealed record StudyDiscoveryProvenance(
    Guid ResearchStudyDiscoveryId,
    Guid LiteratureSearchId,
    string Source,
    string? SourceStudyIdentifier,
    string Query,
    DateTimeOffset SearchedAt,
    DateTimeOffset DiscoveredAt);

public sealed record SourceMaterialProvenance(
    Guid SourceMaterialId,
    Guid StudyId,
    SourceMaterialType Type,
    string Provider,
    string? ProviderSourceId,
    string RetrievalMethod,
    string ContentHash,
    int ContentVersion,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? SourceUpdatedAt,
    SourceMaterialAccessStatus AccessStatus,
    int CharacterCount,
    bool WasTruncated,
    bool IsCurrent,
    IReadOnlyCollection<string> SectionNames);

public sealed record EvidenceExtractionProvenance(
    Guid EvidenceExtractionId,
    Guid StudyId,
    Guid? SourceMaterialId,
    EvidenceExtractionStatus Status,
    EvidenceExtractionSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    string? Provider,
    string? Model,
    string PromptVersion,
    DateTimeOffset ExtractedAt,
    int EvidenceCount,
    bool GroundingValidated);

public sealed record EvidenceProvenance(
    Guid EvidenceId,
    Guid EvidenceExtractionId,
    string Outcome,
    string ResultSummary,
    string SupportingText,
    EvidenceDirection Direction,
    EvidenceSourceScope SourceScope,
    DateTimeOffset ExtractedAt,
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
    decimal? PValue);

public sealed record EvidenceEvaluationProvenance(
    Guid EvidenceEvaluationId,
    Guid StudyId,
    EvidenceEvaluationStatus Status,
    EvidenceEvaluationSkipReason? SkipReason,
    EvidenceSourceScope SourceScope,
    IReadOnlyCollection<Guid> EvidenceIds,
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
    bool HasSampleSize,
    bool HasEffectEstimate,
    bool HasConfidenceInterval,
    bool HasPValue,
    bool HasComparator,
    int UnknownDomainCount,
    int InsufficientSourceDomainCount);

public sealed record ResearchReportClaimProvenance(
    Guid ResearchReportId,
    Guid ResearchReportClaimId,
    ResearchReportClaimType ClaimType,
    ResearchReportClaimDirection Direction,
    string Text,
    int Ordinal,
    IReadOnlyCollection<Guid> EvidenceIds,
    ResearchClaimGroundingStatus GroundingStatus = ResearchClaimGroundingStatus.LegacyUnverified,
    ResearchClaimSemantics? Semantics = null);

public sealed record QuantitativeContributionProvenance(
    Guid ArtifactId,
    string GroupKey,
    string AnalysisMethod,
    int Ordinal,
    Guid EvidenceId,
    Guid StudyId,
    Guid EvidenceExtractionId,
    Guid SourceMaterialId);

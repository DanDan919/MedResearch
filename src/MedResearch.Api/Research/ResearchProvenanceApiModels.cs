namespace MedResearch.Api.Research;

public sealed record ResearchProvenanceResponse(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    ResearchProvenanceCoverageResponse Coverage,
    IReadOnlyCollection<ResearchPlanProvenanceResponse> Plans,
    IReadOnlyCollection<LiteratureSearchProvenanceResponse> Searches,
    IReadOnlyCollection<StudyProvenanceResponse> Studies,
    IReadOnlyCollection<ResearchReportClaimProvenanceResponse> ReportClaims,
    IReadOnlyCollection<QuantitativeContributionProvenanceResponse> QuantitativeContributions,
    IReadOnlyCollection<LiteratureProviderAttemptResponse> ProviderAttempts);

public sealed record LiteratureProviderAttemptResponse(
    Guid AttemptId, Guid ResearchPlanId, string Source, string Query,
    string Status, int? ResultCount, string? FailureCategory,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, Guid? LiteratureSearchId);

public sealed record ResearchProvenanceCoverageResponse(
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

public sealed record ResearchPlanProvenanceResponse(
    Guid ResearchPlanId,
    string OriginalQuestion,
    IReadOnlyCollection<string> SearchQueries,
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset GeneratedAt);

public sealed record LiteratureSearchProvenanceResponse(
    Guid LiteratureSearchId,
    Guid? ResearchPlanId,
    string Source,
    string Query,
    DateTimeOffset SearchedAt,
    int ResultCount,
    int PersistedStudyCount,
    int DuplicateStudyCount,
    string ResultStatus);

public sealed record StudyProvenanceResponse(
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
    IReadOnlyCollection<StudyDiscoveryProvenanceResponse> DiscoveryPaths,
    IReadOnlyCollection<SourceMaterialProvenanceResponse> SourceMaterials,
    IReadOnlyCollection<EvidenceExtractionProvenanceResponse> Extractions,
    IReadOnlyCollection<EvidenceProvenanceResponse> Evidence,
    IReadOnlyCollection<EvidenceEvaluationProvenanceResponse> Evaluations);

public sealed record StudyDiscoveryProvenanceResponse(
    Guid ResearchStudyDiscoveryId,
    Guid LiteratureSearchId,
    string Source,
    string? SourceStudyIdentifier,
    string Query,
    DateTimeOffset SearchedAt,
    DateTimeOffset DiscoveredAt);

public sealed record SourceMaterialProvenanceResponse(
    Guid SourceMaterialId,
    Guid StudyId,
    string Type,
    string Provider,
    string? ProviderSourceId,
    string RetrievalMethod,
    string ContentHash,
    int ContentVersion,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? SourceUpdatedAt,
    string AccessStatus,
    int CharacterCount,
    bool WasTruncated,
    bool IsCurrent,
    IReadOnlyCollection<string> SectionNames);

public sealed record EvidenceExtractionProvenanceResponse(
    Guid EvidenceExtractionId,
    Guid StudyId,
    Guid? SourceMaterialId,
    string Status,
    string? SkipReason,
    string SourceScope,
    string? Provider,
    string? Model,
    string PromptVersion,
    DateTimeOffset ExtractedAt,
    int EvidenceCount,
    bool GroundingValidated);

public sealed record EvidenceProvenanceResponse(
    Guid EvidenceId,
    Guid EvidenceExtractionId,
    string Outcome,
    string ResultSummary,
    string SupportingText,
    string Direction,
    string SourceScope,
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

public sealed record EvidenceEvaluationProvenanceResponse(
    Guid EvidenceEvaluationId,
    Guid StudyId,
    string Status,
    string? SkipReason,
    string SourceScope,
    IReadOnlyCollection<Guid> EvidenceIds,
    string? EvaluatorProvider,
    string? EvaluatorModel,
    string PromptVersion,
    DateTimeOffset EvaluatedAt,
    string StudyDesign,
    string SampleInformation,
    string ComparatorPresence,
    string? ComparatorDescription,
    string Randomization,
    string Blinding,
    string AllocationConcealment,
    string AttritionMissingData,
    string Precision,
    string Directness,
    string OverallConfidence,
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

public sealed record ResearchReportClaimProvenanceResponse(
    Guid ResearchReportId,
    Guid ResearchReportClaimId,
    string ClaimType,
    string Direction,
    string Text,
    int Ordinal,
    IReadOnlyCollection<Guid> EvidenceIds,
    string GroundingStatus = "LegacyUnverified",
    ResearchClaimSemanticsResponse? Semantics = null);

public sealed record QuantitativeContributionProvenanceResponse(
    Guid ArtifactId,
    string GroupKey,
    string AnalysisMethod,
    int Ordinal,
    Guid EvidenceId,
    Guid StudyId,
    Guid EvidenceExtractionId,
    Guid SourceMaterialId);

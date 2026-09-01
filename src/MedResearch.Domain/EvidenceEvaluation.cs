namespace MedResearch.Domain;

public sealed class EvidenceEvaluation
{
    public EvidenceEvaluation(
        Guid id,
        Guid researchRunId,
        Guid studyId,
        Guid[] evidenceIds,
        EvidenceEvaluationStatus status,
        EvidenceEvaluationSkipReason? skipReason,
        EvidenceSourceScope sourceScope,
        string? evaluatorProvider,
        string? evaluatorModel,
        string promptVersion,
        DateTimeOffset evaluatedAt,
        StudyDesignClassification studyDesign,
        MethodologicalAssessmentState sampleInformation,
        ComparatorPresence comparatorPresence,
        string? comparatorDescription,
        MethodologicalAssessmentState randomization,
        MethodologicalAssessmentState blinding,
        MethodologicalAssessmentState allocationConcealment,
        MethodologicalAssessmentState attritionMissingData,
        MethodologicalAssessmentState precision,
        DirectnessRating directness,
        MethodologicalConfidence overallConfidence,
        string rationale,
        string[]? reportingLimitations,
        string[]? authorReportedLimitations,
        bool hasSampleSize,
        bool hasEffectEstimate,
        bool hasConfidenceInterval,
        bool hasPValue,
        bool hasComparator,
        int unknownDomainCount,
        int insufficientSourceDomainCount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Evidence evaluation id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (evidenceIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Evidence ids cannot contain empty values.", nameof(evidenceIds));
        }

        if (status == EvidenceEvaluationStatus.Completed && evidenceIds.Length == 0)
        {
            throw new ArgumentException("Completed evaluations require at least one evidence id.", nameof(evidenceIds));
        }

        if (status == EvidenceEvaluationStatus.Skipped && skipReason is null)
        {
            throw new ArgumentException("Skipped evaluations require a skip reason.", nameof(skipReason));
        }

        if (status == EvidenceEvaluationStatus.Completed && skipReason is not null)
        {
            throw new ArgumentException("Completed evaluations cannot have a skip reason.", nameof(skipReason));
        }

        if (string.IsNullOrWhiteSpace(promptVersion))
        {
            throw new ArgumentException("Evidence evaluation prompt version is required.", nameof(promptVersion));
        }

        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw new ArgumentException("Evidence evaluation rationale is required.", nameof(rationale));
        }

        if (unknownDomainCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unknownDomainCount), "Unknown domain count cannot be negative.");
        }

        if (insufficientSourceDomainCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(insufficientSourceDomainCount), "Insufficient source domain count cannot be negative.");
        }

        Id = id;
        ResearchRunId = researchRunId;
        StudyId = studyId;
        EvidenceIds = evidenceIds.Distinct().ToArray();
        Status = status;
        SkipReason = skipReason;
        SourceScope = sourceScope;
        EvaluatorProvider = NormalizeOptional(evaluatorProvider);
        EvaluatorModel = NormalizeOptional(evaluatorModel);
        PromptVersion = NormalizeRequired(promptVersion, nameof(promptVersion));
        EvaluatedAt = evaluatedAt;
        StudyDesign = studyDesign;
        SampleInformation = sampleInformation;
        ComparatorPresence = comparatorPresence;
        ComparatorDescription = NormalizeOptional(comparatorDescription);
        Randomization = randomization;
        Blinding = blinding;
        AllocationConcealment = allocationConcealment;
        AttritionMissingData = attritionMissingData;
        Precision = precision;
        Directness = directness;
        OverallConfidence = overallConfidence;
        Rationale = NormalizeRequired(rationale, nameof(rationale));
        ReportingLimitations = NormalizeCollection(reportingLimitations ?? []);
        AuthorReportedLimitations = NormalizeCollection(authorReportedLimitations ?? []);
        HasSampleSize = hasSampleSize;
        HasEffectEstimate = hasEffectEstimate;
        HasConfidenceInterval = hasConfidenceInterval;
        HasPValue = hasPValue;
        HasComparator = hasComparator;
        UnknownDomainCount = unknownDomainCount;
        InsufficientSourceDomainCount = insufficientSourceDomainCount;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid StudyId { get; }

    public Guid[] EvidenceIds { get; }

    public EvidenceEvaluationStatus Status { get; }

    public EvidenceEvaluationSkipReason? SkipReason { get; }

    public EvidenceSourceScope SourceScope { get; }

    public string? EvaluatorProvider { get; }

    public string? EvaluatorModel { get; }

    public string PromptVersion { get; }

    public DateTimeOffset EvaluatedAt { get; }

    public StudyDesignClassification StudyDesign { get; }

    public MethodologicalAssessmentState SampleInformation { get; }

    public ComparatorPresence ComparatorPresence { get; }

    public string? ComparatorDescription { get; }

    public MethodologicalAssessmentState Randomization { get; }

    public MethodologicalAssessmentState Blinding { get; }

    public MethodologicalAssessmentState AllocationConcealment { get; }

    public MethodologicalAssessmentState AttritionMissingData { get; }

    public MethodologicalAssessmentState Precision { get; }

    public DirectnessRating Directness { get; }

    public MethodologicalConfidence OverallConfidence { get; }

    public string Rationale { get; }

    public string[] ReportingLimitations { get; }

    public string[] AuthorReportedLimitations { get; }

    public bool HasSampleSize { get; }

    public bool HasEffectEstimate { get; }

    public bool HasConfidenceInterval { get; }

    public bool HasPValue { get; }

    public bool HasComparator { get; }

    public int UnknownDomainCount { get; }

    public int InsufficientSourceDomainCount { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        return NormalizeOptional(value) ?? throw new ArgumentException("Value is required.", parameterName);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] NormalizeCollection(string[] values)
    {
        return values
            .Select(value => NormalizeOptional(value))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

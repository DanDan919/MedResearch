namespace MedResearch.Domain;

public sealed class ResearchReport
{
    public ResearchReport(
        Guid id,
        Guid researchRunId,
        ResearchReportStatus status,
        ResearchReportInsufficientEvidenceReason? insufficientEvidenceReason,
        string executiveSummary,
        string evidenceSummary,
        string conflictSummary,
        string limitationsSummary,
        string conclusion,
        SynthesisConfidence synthesisConfidence,
        string? synthesizerProvider,
        string? synthesizerModel,
        string promptVersion,
        DateTimeOffset generatedAt,
        int discoveredStudyCount,
        int extractedStudyCount,
        int evaluatedStudyCount,
        int evidenceFindingCount,
        int includedStudyCount,
        int includedEvidenceFindingCount,
        int claimCount,
        int searchQueryCount,
        int studiesWithNoExtractableEvidence,
        int studiesWithInsufficientEvaluationSource,
        bool potentialConflictDetected,
        bool evidenceTruncated,
        bool usesAbstractLevelEvidenceOnly,
        string[]? searchedSources,
        string[]? deterministicLimitations)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research report id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (status == ResearchReportStatus.InsufficientEvidence && insufficientEvidenceReason is null)
        {
            throw new ArgumentException("Insufficient-evidence reports require a reason.", nameof(insufficientEvidenceReason));
        }

        if (status == ResearchReportStatus.Completed && insufficientEvidenceReason is not null)
        {
            throw new ArgumentException("Completed reports cannot have an insufficient-evidence reason.", nameof(insufficientEvidenceReason));
        }

        if (string.IsNullOrWhiteSpace(promptVersion))
        {
            throw new ArgumentException("Research report prompt version is required.", nameof(promptVersion));
        }

        ValidateNonNegative(discoveredStudyCount, nameof(discoveredStudyCount));
        ValidateNonNegative(extractedStudyCount, nameof(extractedStudyCount));
        ValidateNonNegative(evaluatedStudyCount, nameof(evaluatedStudyCount));
        ValidateNonNegative(evidenceFindingCount, nameof(evidenceFindingCount));
        ValidateNonNegative(includedStudyCount, nameof(includedStudyCount));
        ValidateNonNegative(includedEvidenceFindingCount, nameof(includedEvidenceFindingCount));
        ValidateNonNegative(claimCount, nameof(claimCount));
        ValidateNonNegative(searchQueryCount, nameof(searchQueryCount));
        ValidateNonNegative(studiesWithNoExtractableEvidence, nameof(studiesWithNoExtractableEvidence));
        ValidateNonNegative(studiesWithInsufficientEvaluationSource, nameof(studiesWithInsufficientEvaluationSource));

        Id = id;
        ResearchRunId = researchRunId;
        Status = status;
        InsufficientEvidenceReason = insufficientEvidenceReason;
        ExecutiveSummary = NormalizeRequired(executiveSummary, nameof(executiveSummary));
        EvidenceSummary = NormalizeRequired(evidenceSummary, nameof(evidenceSummary));
        ConflictSummary = NormalizeRequired(conflictSummary, nameof(conflictSummary));
        LimitationsSummary = NormalizeRequired(limitationsSummary, nameof(limitationsSummary));
        Conclusion = NormalizeRequired(conclusion, nameof(conclusion));
        SynthesisConfidence = synthesisConfidence;
        SynthesizerProvider = NormalizeOptional(synthesizerProvider);
        SynthesizerModel = NormalizeOptional(synthesizerModel);
        PromptVersion = NormalizeRequired(promptVersion, nameof(promptVersion));
        GeneratedAt = generatedAt;
        DiscoveredStudyCount = discoveredStudyCount;
        ExtractedStudyCount = extractedStudyCount;
        EvaluatedStudyCount = evaluatedStudyCount;
        EvidenceFindingCount = evidenceFindingCount;
        IncludedStudyCount = includedStudyCount;
        IncludedEvidenceFindingCount = includedEvidenceFindingCount;
        ClaimCount = claimCount;
        SearchQueryCount = searchQueryCount;
        StudiesWithNoExtractableEvidence = studiesWithNoExtractableEvidence;
        StudiesWithInsufficientEvaluationSource = studiesWithInsufficientEvaluationSource;
        PotentialConflictDetected = potentialConflictDetected;
        EvidenceTruncated = evidenceTruncated;
        UsesAbstractLevelEvidenceOnly = usesAbstractLevelEvidenceOnly;
        SearchedSources = NormalizeCollection(searchedSources ?? []);
        DeterministicLimitations = NormalizeCollection(deterministicLimitations ?? []);
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public ResearchReportStatus Status { get; }

    public ResearchReportInsufficientEvidenceReason? InsufficientEvidenceReason { get; }

    public string ExecutiveSummary { get; }

    public string EvidenceSummary { get; }

    public string ConflictSummary { get; }

    public string LimitationsSummary { get; }

    public string Conclusion { get; }

    public SynthesisConfidence SynthesisConfidence { get; }

    public string? SynthesizerProvider { get; }

    public string? SynthesizerModel { get; }

    public string PromptVersion { get; }

    public DateTimeOffset GeneratedAt { get; }

    public int DiscoveredStudyCount { get; }

    public int ExtractedStudyCount { get; }

    public int EvaluatedStudyCount { get; }

    public int EvidenceFindingCount { get; }

    public int IncludedStudyCount { get; }

    public int IncludedEvidenceFindingCount { get; }

    public int ClaimCount { get; }

    public int SearchQueryCount { get; }

    public int StudiesWithNoExtractableEvidence { get; }

    public int StudiesWithInsufficientEvaluationSource { get; }

    public bool PotentialConflictDetected { get; }

    public bool EvidenceTruncated { get; }

    public bool UsesAbstractLevelEvidenceOnly { get; }

    public string[] SearchedSources { get; }

    public string[] DeterministicLimitations { get; }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }
    }

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
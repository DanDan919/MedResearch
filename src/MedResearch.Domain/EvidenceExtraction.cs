namespace MedResearch.Domain;

public sealed class EvidenceExtraction
{
    public EvidenceExtraction(
        Guid id,
        Guid researchRunId,
        Guid studyId,
        EvidenceExtractionStatus status,
        EvidenceExtractionSkipReason? skipReason,
        EvidenceSourceScope sourceScope,
        string? provider,
        string? model,
        string promptVersion,
        DateTimeOffset extractedAt,
        int evidenceCount,
        bool groundingValidated)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Evidence extraction id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (string.IsNullOrWhiteSpace(promptVersion))
        {
            throw new ArgumentException("Evidence extraction prompt version is required.", nameof(promptVersion));
        }

        if (status == EvidenceExtractionStatus.Skipped && skipReason is null)
        {
            throw new ArgumentException("Skipped evidence extractions require a skip reason.", nameof(skipReason));
        }

        if (status == EvidenceExtractionStatus.Completed && skipReason is not null)
        {
            throw new ArgumentException("Completed evidence extractions cannot have a skip reason.", nameof(skipReason));
        }

        if (evidenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceCount), "Evidence count cannot be negative.");
        }

        Id = id;
        ResearchRunId = researchRunId;
        StudyId = studyId;
        Status = status;
        SkipReason = skipReason;
        SourceScope = sourceScope;
        Provider = NormalizeOptional(provider);
        Model = NormalizeOptional(model);
        PromptVersion = NormalizeRequired(promptVersion, nameof(promptVersion));
        ExtractedAt = extractedAt;
        EvidenceCount = evidenceCount;
        GroundingValidated = groundingValidated;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid StudyId { get; }

    public EvidenceExtractionStatus Status { get; }

    public EvidenceExtractionSkipReason? SkipReason { get; }

    public EvidenceSourceScope SourceScope { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public string PromptVersion { get; }

    public DateTimeOffset ExtractedAt { get; }

    public int EvidenceCount { get; }

    public bool GroundingValidated { get; }

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
}

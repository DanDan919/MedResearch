namespace MedResearch.Domain;

public sealed class Evidence
{
    public Evidence(
        Guid id,
        Guid researchRunId,
        Guid studyId,
        Guid evidenceExtractionId,
        string outcome,
        string resultSummary,
        string supportingText,
        EvidenceDirection direction,
        EvidenceSourceScope sourceScope,
        DateTimeOffset extractedAt,
        bool groundingValidated,
        string? population,
        string? exposureOrIntervention,
        string? comparator,
        string? studyDesign,
        int? sampleSize,
        string? effectMeasure,
        decimal? effectValue,
        decimal? confidenceIntervalLower,
        decimal? confidenceIntervalUpper,
        decimal? pValue)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Evidence id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (evidenceExtractionId == Guid.Empty)
        {
            throw new ArgumentException("Evidence extraction id cannot be empty.", nameof(evidenceExtractionId));
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new ArgumentException("Evidence outcome is required.", nameof(outcome));
        }

        if (string.IsNullOrWhiteSpace(resultSummary))
        {
            throw new ArgumentException("Evidence result summary is required.", nameof(resultSummary));
        }

        if (string.IsNullOrWhiteSpace(supportingText))
        {
            throw new ArgumentException("Evidence supporting text is required.", nameof(supportingText));
        }

        if (sampleSize is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Sample size must be positive when present.");
        }

        if (pValue is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(pValue), "P-value must be between 0 and 1 when present.");
        }

        Id = id;
        ResearchRunId = researchRunId;
        StudyId = studyId;
        EvidenceExtractionId = evidenceExtractionId;
        Outcome = NormalizeRequired(outcome, nameof(outcome));
        ResultSummary = NormalizeRequired(resultSummary, nameof(resultSummary));
        SupportingText = supportingText.Trim();
        Direction = direction;
        SourceScope = sourceScope;
        ExtractedAt = extractedAt;
        GroundingValidated = groundingValidated;
        Population = NormalizeOptional(population);
        ExposureOrIntervention = NormalizeOptional(exposureOrIntervention);
        Comparator = NormalizeOptional(comparator);
        StudyDesign = NormalizeOptional(studyDesign);
        SampleSize = sampleSize;
        EffectMeasure = NormalizeOptional(effectMeasure);
        EffectValue = effectValue;
        ConfidenceIntervalLower = confidenceIntervalLower;
        ConfidenceIntervalUpper = confidenceIntervalUpper;
        PValue = pValue;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid StudyId { get; }

    public Guid EvidenceExtractionId { get; }

    public string Outcome { get; }

    public string ResultSummary { get; }

    public string SupportingText { get; }

    public EvidenceDirection Direction { get; }

    public EvidenceSourceScope SourceScope { get; }

    public DateTimeOffset ExtractedAt { get; }

    public bool GroundingValidated { get; }

    public string? Population { get; }

    public string? ExposureOrIntervention { get; }

    public string? Comparator { get; }

    public string? StudyDesign { get; }

    public int? SampleSize { get; }

    public string? EffectMeasure { get; }

    public decimal? EffectValue { get; }

    public decimal? ConfidenceIntervalLower { get; }

    public decimal? ConfidenceIntervalUpper { get; }

    public decimal? PValue { get; }

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

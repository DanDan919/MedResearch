namespace MedResearch.Domain;

public sealed class ResearchPlan
{
    public ResearchPlan(
        Guid id,
        Guid researchRunId,
        Guid researchQuestionId,
        string originalQuestion,
        string? population,
        string? exposureOrIntervention,
        string? comparator,
        string[]? outcomes,
        string[]? preferredStudyTypes,
        string[] searchQueries,
        string[]? exclusionHints,
        string provider,
        string model,
        string promptVersion,
        DateTimeOffset generatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research plan id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (researchQuestionId == Guid.Empty)
        {
            throw new ArgumentException("Research question id cannot be empty.", nameof(researchQuestionId));
        }

        if (string.IsNullOrWhiteSpace(originalQuestion))
        {
            throw new ArgumentException("Original question is required.", nameof(originalQuestion));
        }

        if (searchQueries is null || searchQueries.Length == 0)
        {
            throw new ArgumentException("At least one search query is required.", nameof(searchQueries));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Planning provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Planning model is required.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(promptVersion))
        {
            throw new ArgumentException("Planning prompt version is required.", nameof(promptVersion));
        }

        Id = id;
        ResearchRunId = researchRunId;
        ResearchQuestionId = researchQuestionId;
        OriginalQuestion = NormalizeRequired(originalQuestion, nameof(originalQuestion));
        Population = NormalizeOptional(population);
        ExposureOrIntervention = NormalizeOptional(exposureOrIntervention);
        Comparator = NormalizeOptional(comparator);
        Outcomes = NormalizeCollection(outcomes ?? []);
        PreferredStudyTypes = NormalizeCollection(preferredStudyTypes ?? []);
        SearchQueries = NormalizeCollection(searchQueries);
        ExclusionHints = NormalizeCollection(exclusionHints ?? []);
        Provider = NormalizeRequired(provider, nameof(provider));
        Model = NormalizeRequired(model, nameof(model));
        PromptVersion = NormalizeRequired(promptVersion, nameof(promptVersion));
        GeneratedAt = generatedAt;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid ResearchQuestionId { get; }

    public string OriginalQuestion { get; }

    public string? Population { get; }

    public string? ExposureOrIntervention { get; }

    public string? Comparator { get; }

    public string[] Outcomes { get; }

    public string[] PreferredStudyTypes { get; }

    public string[] SearchQueries { get; }

    public string[] ExclusionHints { get; }

    public string Provider { get; }

    public string Model { get; }

    public string PromptVersion { get; }

    public DateTimeOffset GeneratedAt { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = NormalizeOptional(value);

        return normalized ?? throw new ArgumentException("Value is required.", parameterName);
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

namespace MedResearch.Domain;

public sealed class LiteratureSearch
{
    public LiteratureSearch(
        Guid id,
        Guid researchRunId,
        string source,
        string query,
        DateTimeOffset searchedAt,
        int resultCount,
        int persistedStudyCount,
        int duplicateStudyCount,
        Guid? researchPlanId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Literature search id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (researchPlanId == Guid.Empty)
        {
            throw new ArgumentException("Research plan id cannot be empty when provided.", nameof(researchPlanId));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Literature search source is required.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Literature search query is required.", nameof(query));
        }

        if (resultCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resultCount), "Result count cannot be negative.");
        }

        if (persistedStudyCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(persistedStudyCount), "Persisted study count cannot be negative.");
        }

        if (duplicateStudyCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateStudyCount), "Duplicate study count cannot be negative.");
        }

        Id = id;
        ResearchRunId = researchRunId;
        ResearchPlanId = researchPlanId;
        Source = source.Trim();
        Query = query.Trim();
        SearchedAt = searchedAt;
        ResultCount = resultCount;
        PersistedStudyCount = persistedStudyCount;
        DuplicateStudyCount = duplicateStudyCount;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid? ResearchPlanId { get; }

    public string Source { get; }

    public string Query { get; }

    public DateTimeOffset SearchedAt { get; }

    public int ResultCount { get; }

    public int PersistedStudyCount { get; }

    public int DuplicateStudyCount { get; }
}

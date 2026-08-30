namespace MedResearch.Domain;

public sealed class ResearchStudyDiscovery
{
    public ResearchStudyDiscovery(
        Guid id,
        Guid researchRunId,
        Guid literatureSearchId,
        Guid studyId,
        string source,
        string? sourceStudyIdentifier,
        DateTimeOffset discoveredAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research study discovery id cannot be empty.", nameof(id));
        }

        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (literatureSearchId == Guid.Empty)
        {
            throw new ArgumentException("Literature search id cannot be empty.", nameof(literatureSearchId));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Discovery source is required.", nameof(source));
        }

        Id = id;
        ResearchRunId = researchRunId;
        LiteratureSearchId = literatureSearchId;
        StudyId = studyId;
        Source = source.Trim();
        SourceStudyIdentifier = string.IsNullOrWhiteSpace(sourceStudyIdentifier) ? null : sourceStudyIdentifier.Trim();
        DiscoveredAt = discoveredAt;
    }

    public Guid Id { get; }

    public Guid ResearchRunId { get; }

    public Guid LiteratureSearchId { get; }

    public Guid StudyId { get; }

    public string Source { get; }

    public string? SourceStudyIdentifier { get; }

    public DateTimeOffset DiscoveredAt { get; }
}

namespace MedResearch.Domain;

public sealed class Study
{
    public Study(
        Guid id,
        string title,
        string? abstractText,
        string? doi,
        string? pmid,
        string? journal,
        DateOnly? publicationDate,
        string source)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Study title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Study source is required.", nameof(source));
        }

        Id = id;
        Title = title.Trim();
        Abstract = string.IsNullOrWhiteSpace(abstractText) ? null : abstractText.Trim();
        Doi = string.IsNullOrWhiteSpace(doi) ? null : doi.Trim();
        Pmid = string.IsNullOrWhiteSpace(pmid) ? null : pmid.Trim();
        Journal = string.IsNullOrWhiteSpace(journal) ? null : journal.Trim();
        PublicationDate = publicationDate;
        Source = source.Trim();
    }

    public Guid Id { get; }

    public string Title { get; }

    public string? Abstract { get; }

    public string? Doi { get; }

    public string? Pmid { get; }

    public string? Journal { get; }

    public DateOnly? PublicationDate { get; }

    public string Source { get; }
}

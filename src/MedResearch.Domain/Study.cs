namespace MedResearch.Domain;

public sealed class Study
{
    public Study(
        Guid id,
        string title,
        string? @abstract,
        string? doi,
        string? pmid,
        string? journal,
        DateOnly? publicationDate,
        string source)
        : this(
            id,
            title,
            @abstract,
            doi,
            pmid,
            null,
            journal,
            publicationDate,
            publicationDate?.Year,
            publicationDate?.Month,
            publicationDate?.Day,
            [],
            [],
            source)
    {
    }

    public Study(
        Guid id,
        string title,
        string? @abstract,
        string? doi,
        string? pmid,
        string? journal,
        DateOnly? publicationDate,
        int? publicationYear,
        int? publicationMonth,
        int? publicationDay,
        string[]? publicationTypes,
        string[]? authors,
        string source)
        : this(
            id,
            title,
            @abstract,
            doi,
            pmid,
            null,
            journal,
            publicationDate,
            publicationYear,
            publicationMonth,
            publicationDay,
            publicationTypes,
            authors,
            source)
    {
    }

    public Study(
        Guid id,
        string title,
        string? @abstract,
        string? doi,
        string? pmid,
        string? pmcid,
        string? journal,
        DateOnly? publicationDate,
        int? publicationYear,
        int? publicationMonth,
        int? publicationDay,
        string[]? publicationTypes,
        string[]? authors,
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

        ValidatePublicationDateParts(publicationYear, publicationMonth, publicationDay);

        Id = id;
        Title = title.Trim();
        Abstract = NormalizeOptional(@abstract);
        Doi = NormalizeOptional(doi);
        Pmid = NormalizeOptional(pmid);
        Pmcid = NormalizeOptional(pmcid);
        Journal = NormalizeOptional(journal);
        PublicationDate = publicationDate;
        PublicationYear = publicationYear;
        PublicationMonth = publicationMonth;
        PublicationDay = publicationDay;
        PublicationTypes = NormalizeCollection(publicationTypes);
        Authors = NormalizeCollection(authors);
        Source = source.Trim();
    }

    public Guid Id { get; }

    public string Title { get; private set; }

    public string? Abstract { get; private set; }

    public string? Doi { get; private set; }

    public string? Pmid { get; private set; }

    public string? Pmcid { get; private set; }

    public string? Journal { get; private set; }

    public DateOnly? PublicationDate { get; private set; }

    public int? PublicationYear { get; private set; }

    public int? PublicationMonth { get; private set; }

    public int? PublicationDay { get; private set; }

    public string[] PublicationTypes { get; private set; }

    public string[] Authors { get; private set; }

    public string Source { get; private set; }

    public void EnrichMissingMetadata(
        string? @abstract,
        string? doi,
        string? pmid,
        string? pmcid,
        string? journal,
        DateOnly? publicationDate,
        int? publicationYear,
        int? publicationMonth,
        int? publicationDay,
        string[]? publicationTypes,
        string[]? authors)
    {
        ValidatePublicationDateParts(publicationYear, publicationMonth, publicationDay);

        Abstract ??= NormalizeOptional(@abstract);
        Doi ??= NormalizeOptional(doi);
        Pmid ??= NormalizeOptional(pmid);
        Pmcid ??= NormalizeOptional(pmcid);
        Journal ??= NormalizeOptional(journal);
        PublicationDate ??= publicationDate;
        PublicationYear ??= publicationYear;
        PublicationMonth ??= publicationMonth;
        PublicationDay ??= publicationDay;
        PublicationTypes = MergeCollection(PublicationTypes, publicationTypes);
        Authors = MergeCollection(Authors, authors);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] NormalizeCollection(string[]? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] MergeCollection(string[] existing, string[]? incoming)
    {
        return existing
            .Concat(NormalizeCollection(incoming))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidatePublicationDateParts(int? year, int? month, int? day)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Publication year must be a valid calendar year.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Publication month must be between 1 and 12.");
        }

        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Publication day must be between 1 and 31.");
        }

        if (day.HasValue && !month.HasValue)
        {
            throw new ArgumentException("Publication day cannot be stored without a publication month.", nameof(day));
        }

        if (month.HasValue && !year.HasValue)
        {
            throw new ArgumentException("Publication month cannot be stored without a publication year.", nameof(month));
        }
    }
}

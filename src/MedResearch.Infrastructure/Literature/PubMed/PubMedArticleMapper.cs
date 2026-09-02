using System.Text.RegularExpressions;
using System.Xml.Linq;
using MedResearch.Application.Research.Literature;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedArticleMapper
{
    private const string Source = "PubMed";
    private static readonly Regex PmidPattern = new("^[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MedlineYearPattern = new("(?<year>[12][0-9]{3})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyCollection<ScientificStudyCandidate> MapArticles(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            return document
                .Descendants("PubmedArticle")
                .Select(MapArticle)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .ToArray();
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
        {
            throw new PubMedResponseException("PubMed EFetch response could not be parsed as PubMed XML.", exception);
        }
    }

    private static ScientificStudyCandidate? MapArticle(XElement article)
    {
        var citation = article.Element("MedlineCitation");
        var pubmedData = article.Element("PubmedData");
        var articleElement = citation?.Element("Article");

        var pmid = NormalizePmid(citation?.Element("PMID")?.Value);
        var title = NormalizeText(articleElement?.Element("ArticleTitle"));

        if (string.IsNullOrWhiteSpace(pmid) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var publicationParts = ReadPublicationDate(articleElement);

        return new ScientificStudyCandidate(
            pmid,
            ReadDoi(articleElement, pubmedData),
            title,
            ReadAbstract(articleElement),
            Normalize(articleElement?.Element("Journal")?.Element("Title")?.Value)
                ?? Normalize(articleElement?.Element("Journal")?.Element("ISOAbbreviation")?.Value),
            publicationParts.PublicationDate,
            publicationParts.Year,
            publicationParts.Month,
            publicationParts.Day,
            ReadPublicationTypes(articleElement),
            ReadAuthors(articleElement),
            Source);
    }

    private static string? ReadDoi(XElement? articleElement, XElement? pubmedData)
    {
        var articleIdDoi = pubmedData?
            .Element("ArticleIdList")?
            .Elements("ArticleId")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("IdType"), "doi", StringComparison.OrdinalIgnoreCase));

        var electronicLocationDoi = articleElement?
            .Elements("ELocationID")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("EIdType"), "doi", StringComparison.OrdinalIgnoreCase));

        return NormalizeDoi(articleIdDoi?.Value ?? electronicLocationDoi?.Value);
    }

    private static string? ReadAbstract(XElement? articleElement)
    {
        var abstractTexts = articleElement?
            .Element("Abstract")?
            .Elements("AbstractText")
            .Select(element =>
            {
                var text = NormalizeText(element);
                var label = Normalize((string?)element.Attribute("Label"));

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return string.IsNullOrWhiteSpace(label) ? text : $"{label}: {text}";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        if (abstractTexts is null || abstractTexts.Length == 0)
        {
            return null;
        }

        return string.Join(Environment.NewLine, abstractTexts);
    }

    private static IReadOnlyCollection<string> ReadPublicationTypes(XElement? articleElement)
    {
        return articleElement?
            .Element("PublicationTypeList")?
            .Elements("PublicationType")
            .Select(element => NormalizeText(element))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static IReadOnlyCollection<string> ReadAuthors(XElement? articleElement)
    {
        return articleElement?
            .Element("AuthorList")?
            .Elements("Author")
            .Select(ReadAuthor)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static string? ReadAuthor(XElement author)
    {
        var collectiveName = Normalize(author.Element("CollectiveName")?.Value);
        if (!string.IsNullOrWhiteSpace(collectiveName))
        {
            return collectiveName;
        }

        var foreName = Normalize(author.Element("ForeName")?.Value);
        var lastName = Normalize(author.Element("LastName")?.Value);

        return (foreName, lastName) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{foreName} {lastName}",
            (null, { Length: > 0 }) => lastName,
            ({ Length: > 0 }, null) => foreName,
            _ => null
        };
    }

    private static PublicationParts ReadPublicationDate(XElement? articleElement)
    {
        var journalPubDate = articleElement?
            .Element("Journal")?
            .Element("JournalIssue")?
            .Element("PubDate");

        var journalDate = ReadPubDate(journalPubDate);
        if (journalDate.HasAnyPart)
        {
            return journalDate;
        }

        var electronicArticleDate = articleElement?
            .Elements("ArticleDate")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("DateType"), "Electronic", StringComparison.OrdinalIgnoreCase));

        var articleDate = ReadDateElements(electronicArticleDate ?? articleElement?.Element("ArticleDate"));
        return articleDate.HasAnyPart
            ? articleDate
            : new PublicationParts(null, null, null, null);
    }

    private static PublicationParts ReadPubDate(XElement? pubDate)
    {
        if (pubDate is null)
        {
            return new PublicationParts(null, null, null, null);
        }

        var year = ParseInt(pubDate.Element("Year")?.Value);
        var month = ParseMonth(pubDate.Element("Month")?.Value);
        var day = ParseInt(pubDate.Element("Day")?.Value);

        if (!year.HasValue)
        {
            var medlineDate = Normalize(pubDate.Element("MedlineDate")?.Value);
            if (!string.IsNullOrWhiteSpace(medlineDate))
            {
                year = ParseMedlineYear(medlineDate);
                month = ParseMedlineMonth(medlineDate);
            }
        }

        return BuildPublicationParts(year, month, day);
    }

    private static PublicationParts ReadDateElements(XElement? dateElement)
    {
        if (dateElement is null)
        {
            return new PublicationParts(null, null, null, null);
        }

        return BuildPublicationParts(
            ParseInt(dateElement.Element("Year")?.Value),
            ParseMonth(dateElement.Element("Month")?.Value),
            ParseInt(dateElement.Element("Day")?.Value));
    }

    private static PublicationParts BuildPublicationParts(int? year, int? month, int? day)
    {
        DateOnly? publicationDate = null;
        if (year.HasValue && month.HasValue && day.HasValue &&
            DateOnly.TryParse($"{year.Value:0000}-{month.Value:00}-{day.Value:00}", out var parsedDate))
        {
            publicationDate = parsedDate;
        }

        return new PublicationParts(publicationDate, year, month, day);
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(Normalize(value), out var parsed) ? parsed : null;
    }

    private static int? ParseMonth(string? value)
    {
        var normalized = Normalize(value);

        if (normalized is null)
        {
            return null;
        }

        if (int.TryParse(normalized, out var numericMonth))
        {
            return numericMonth is >= 1 and <= 12 ? numericMonth : null;
        }

        var abbreviated = normalized.Length >= 3 ? normalized[..3] : normalized;

        return abbreviated.ToLowerInvariant() switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => null
        };
    }

    private static int? ParseMedlineYear(string medlineDate)
    {
        var match = MedlineYearPattern.Match(medlineDate);
        return match.Success && int.TryParse(match.Groups["year"].Value, out var year)
            ? year
            : null;
    }

    private static int? ParseMedlineMonth(string medlineDate)
    {
        foreach (var token in medlineDate.Split([' ', '-', '/', ';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var month = ParseMonth(token);
            if (month.HasValue)
            {
                return month;
            }
        }

        return null;
    }

    private static string? NormalizePmid(string? value)
    {
        var normalized = Normalize(value);
        return normalized is not null && PmidPattern.IsMatch(normalized)
            ? normalized
            : null;
    }

    private static string? NormalizeDoi(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        foreach (var prefix in new[] { "doi:", "https://doi.org/", "http://doi.org/", "https://dx.doi.org/", "http://dx.doi.org/" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }

        return normalized.Length == 0 ? null : normalized.ToLowerInvariant();
    }

    private static string? NormalizeText(XElement? element)
    {
        return Normalize(element?.Value);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record PublicationParts(DateOnly? PublicationDate, int? Year, int? Month, int? Day)
    {
        public bool HasAnyPart => PublicationDate.HasValue || Year.HasValue || Month.HasValue || Day.HasValue;
    }
}
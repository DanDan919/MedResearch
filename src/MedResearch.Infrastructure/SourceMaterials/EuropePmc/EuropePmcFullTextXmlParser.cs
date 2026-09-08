using System.Xml;
using System.Xml.Linq;

namespace MedResearch.Infrastructure.SourceMaterials.EuropePmc;

public sealed class EuropePmcFullTextXmlParser
{
    private const string ArticleTitle = "article-title";
    private const string Abstract = "abstract";
    private const string Body = "body";
    private const string Back = "back";
    private const string Section = "sec";
    private const string Title = "title";
    private const string Paragraph = "p";
    private const string License = "license";
    private const string LicenseP = "license-p";

    public ParsedEuropePmcFullText Parse(string xml, int maxContentCharacters)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new EuropePmcFullTextResponseException("Europe PMC full-text XML response was empty.");
        }

        if (maxContentCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxContentCharacters), "Max content characters must be positive.");
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var sections = new List<SourceTextSection>();

            var title = ReadElementText(DescendantsNamed(document, ArticleTitle).FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(title))
            {
                sections.Add(new SourceTextSection("Title", title));
            }

            var abstractText = ReadElementText(DescendantsNamed(document, Abstract).FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(abstractText))
            {
                sections.Add(new SourceTextSection("Abstract", abstractText));
            }

            foreach (var section in DescendantsNamed(document, Body).SelectMany(body => ElementsNamed(body, Section)))
            {
                AddSection(section, sections);
            }

            foreach (var section in DescendantsNamed(document, Back).SelectMany(back => ElementsNamed(back, Section)))
            {
                AddSection(section, sections);
            }

            if (sections.Count == 0)
            {
                var fallbackParagraphs = DescendantsNamed(document, Paragraph)
                    .Select(ReadElementText)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Take(20)
                    .ToArray();
                if (fallbackParagraphs.Length > 0)
                {
                    sections.Add(new SourceTextSection("FullText", string.Join("\n\n", fallbackParagraphs)));
                }
            }

            var content = string.Join("\n\n", sections.Select(section => $"## {section.Name}\n{section.Text}"));
            var wasTruncated = false;
            if (content.Length > maxContentCharacters)
            {
                content = content[..maxContentCharacters].TrimEnd();
                wasTruncated = true;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new EuropePmcFullTextResponseException("Europe PMC full-text XML contained no extractable source text.");
            }

            var licenseElement = DescendantsNamed(document, License).FirstOrDefault();
            var licenseUrl = licenseElement?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value;
            var licenseText = ReadElementText(licenseElement is null ? null : DescendantsNamed(licenseElement, LicenseP).FirstOrDefault());

            return new ParsedEuropePmcFullText(
                content,
                wasTruncated,
                sections.Select(section => section.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                NormalizeOptional(licenseText),
                NormalizeOptional(licenseUrl));
        }
        catch (EuropePmcFullTextResponseException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw new EuropePmcFullTextResponseException("Europe PMC full-text XML could not be parsed.", exception);
        }
    }

    private static void AddSection(XElement section, List<SourceTextSection> sections)
    {
        var name = NormalizeOptional(ReadElementText(ElementsNamed(section, Title).FirstOrDefault())) ?? "Other";
        var paragraphs = ElementsNamed(section, Paragraph)
            .Select(ReadElementText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        if (paragraphs.Length == 0)
        {
            return;
        }

        sections.Add(new SourceTextSection(name, string.Join("\n", paragraphs)));
    }

    private static IEnumerable<XElement> DescendantsNamed(XContainer container, string localName)
    {
        return container.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static IEnumerable<XElement> ElementsNamed(XContainer container, string localName)
    {
        return container.Elements().Where(element => element.Name.LocalName == localName);
    }

    private static string? ReadElementText(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var text = string.Join(" ", element.DescendantNodesAndSelf()
            .OfType<XText>()
            .Select(node => node.Value));
        return NormalizeOptional(text);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record SourceTextSection(string Name, string Text);
}

public sealed record ParsedEuropePmcFullText(
    string Content,
    bool WasTruncated,
    IReadOnlyCollection<string> SectionNames,
    string? License,
    string? LicenseUrl);

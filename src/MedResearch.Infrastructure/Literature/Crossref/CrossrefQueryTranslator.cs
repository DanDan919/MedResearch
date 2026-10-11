using System.Text.RegularExpressions;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.EuropePmc;

namespace MedResearch.Infrastructure.Literature.Crossref;

internal static partial class CrossrefQueryTranslator
{
    // Crossref query is ranked lexical retrieval, not Boolean eligibility.
    // Expand positive operands only; never silently drop a negative constraint.
    public static string Translate(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 2000) throw Unsupported();
        var prepared = EuropePmcQueryTranslator.Translate(query);
        var words = new List<string>();
        var depth = 0;
        var quoted = false;
        var remainder = Fields().Replace(prepared, "");
        foreach (Match token in Tokens().Matches(remainder))
        {
            var value = token.Value;
            if (value == "\"") { quoted = !quoted; continue; }
            if (!quoted && value == "(") { if (++depth > 32) throw Unsupported(); continue; }
            if (!quoted && value == ")") { if (--depth < 0) throw Unsupported(); continue; }
            if (value == "NOT") throw Unsupported();
            if (value is "AND" or "OR") continue;
            if (value is ":" or "[" or "]" or "\\" || value.StartsWith('-')) throw Unsupported();
            if (!Word().IsMatch(value)) throw Unsupported();
            words.Add(value.TrimEnd('*'));
        }
        if (quoted || depth != 0 || words.Count == 0) throw Unsupported();
        return string.Join(' ', words.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static ScientificLiteratureSourceException Unsupported() => new(
        "Crossref supplemental lexical search cannot safely prepare this query. Negative constraints and unsupported fields require another search strategy.",
        LiteratureProviderFailureCategory.ProviderProtocolError);

    [GeneratedRegex(@"\b(?:TITLE_ABS|TITLE):(?=\()", RegexOptions.CultureInvariant)]
    private static partial Regex Fields();
    [GeneratedRegex("[\\p{L}\\p{N}_-]+\\*?|[^\\s]", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N}_-]*\*?$", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}

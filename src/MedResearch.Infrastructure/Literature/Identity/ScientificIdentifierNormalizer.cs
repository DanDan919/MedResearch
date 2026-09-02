using System.Text.RegularExpressions;

namespace MedResearch.Infrastructure.Literature.Identity;

public static partial class ScientificIdentifierNormalizer
{
    public static string? NormalizePmid(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        return normalized is not null && PmidPattern().IsMatch(normalized)
            ? normalized
            : null;
    }

    public static string? NormalizePmcid(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.StartsWith("PMC", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized[3..];
            return PmcidDigitsPattern().IsMatch(suffix)
                ? $"PMC{suffix}"
                : null;
        }

        return PmcidDigitsPattern().IsMatch(normalized)
            ? $"PMC{normalized}"
            : null;
    }

    public static string? NormalizeDoi(string? value)
    {
        var normalized = NormalizeWhitespace(value);
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

    public static string? NormalizeWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PmidPattern();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PmcidDigitsPattern();
}
using System.Text.Json;
using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature.Identity;

namespace MedResearch.Infrastructure.Literature.Crossref;

internal static class CrossrefResponseMapper
{
    public static CrossrefPage Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Text(root, "status") != "ok" || Text(root, "message-type") != "work-list" ||
                !root.TryGetProperty("message", out var message) || !message.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || !message.TryGetProperty("total-results", out var total) ||
                !total.TryGetInt64(out var count) || count < 0)
                throw new CrossrefResponseException();
            var candidates = new List<ScientificStudyCandidate>();
            foreach (var item in items.EnumerateArray())
            {
                var doi = ScientificIdentifierNormalizer.NormalizeDoi(Text(item, "DOI"));
                var title = Strings(item, "title").FirstOrDefault();
                if (!HasUsableDoi(doi) || title is null || title.Length > 1000 || Text(item, "type") != "journal-article") continue;
                var date = Date(item);
                candidates.Add(new ScientificStudyCandidate(null, null, doi, title,
                    ScientificIdentifierNormalizer.NormalizeWhitespace(Text(item, "abstract")),
                    Strings(item, "container-title").FirstOrDefault(value => value.Length <= 512), date.Date, date.Year, date.Month, date.Day,
                    ["journal-article"], Authors(item), doi, ScientificLiteratureSourceNames.Crossref));
            }
            return new CrossrefPage(candidates, items.GetArrayLength(), Text(message, "next-cursor"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new CrossrefResponseException();
        }
    }

    private static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasUsableDoi(string? doi)
    {
        if (doi is null || doi.Length > 255 || !doi.StartsWith("10.", StringComparison.Ordinal) || doi.Any(char.IsWhiteSpace)) return false;
        var slash = doi.IndexOf('/');
        return slash > 3 && slash < doi.Length - 1 && doi.AsSpan(3, slash - 3).ContainsAnyExceptInRange('0', '9') == false;
    }

    private static string[] Strings(JsonElement element, string key) => element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ?
            ScientificIdentifierNormalizer.NormalizeWhitespace(v.GetString()) : null).OfType<string>().ToArray() : [];

    private static string[] Authors(JsonElement item)
    {
        if (!item.TryGetProperty("author", out var authors) || authors.ValueKind != JsonValueKind.Array) return [];
        return authors.EnumerateArray().Select(author => ScientificIdentifierNormalizer.NormalizeWhitespace(
            Text(author, "name") ?? string.Join(' ', new[] { Text(author, "given"), Text(author, "family") }.OfType<string>())))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    private static (DateOnly? Date, int? Year, int? Month, int? Day) Date(JsonElement item)
    {
        // Index/deposit/create timestamps are not publication dates.
        foreach (var key in new[] { "published", "published-print", "published-online", "issued" })
        {
            if (!item.TryGetProperty(key, out var value) || !value.TryGetProperty("date-parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0 || parts[0].ValueKind != JsonValueKind.Array) continue;
            var numbers = parts[0].EnumerateArray().Select(v => v.TryGetInt32(out var n) ? (int?)n : null).Take(3).ToArray();
            if (numbers.Length == 0 || numbers[0] is not { } year || year is < 1 or > 9999) continue;
            var month = numbers.Length > 1 && numbers[1] is >= 1 and <= 12 ? numbers[1] : null;
            var day = month.HasValue && numbers.Length > 2 && numbers[2] >= 1 && numbers[2] <= DateTime.DaysInMonth(year, month.Value) ? numbers[2] : null;
            DateOnly? date = day.HasValue ? new DateOnly(year, month!.Value, day.Value) : null;
            return (date, year, month, day);
        }
        return (null, null, null, null);
    }
}

internal sealed record CrossrefPage(IReadOnlyCollection<ScientificStudyCandidate> Candidates, int RawCount, string? NextCursor);
internal sealed class CrossrefResponseException : Exception;

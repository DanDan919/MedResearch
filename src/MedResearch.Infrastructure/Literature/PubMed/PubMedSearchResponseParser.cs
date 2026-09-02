using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedSearchResponseParser
{
    private static readonly Regex PmidPattern = new("^[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PubMedSearchResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("esearchresult", out var searchResult) ||
                !searchResult.TryGetProperty("idlist", out var idList) ||
                idList.ValueKind != JsonValueKind.Array)
            {
                throw new PubMedResponseException("PubMed ESearch response did not contain an idlist.");
            }

            var count = ReadCount(searchResult);
            var pmids = idList
                .EnumerateArray()
                .Select(ReadPmid)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new PubMedSearchResult(pmids, count);
        }
        catch (JsonException exception)
        {
            throw new PubMedResponseException("PubMed ESearch response was not valid JSON.", exception);
        }
    }

    public IReadOnlyList<string> ParsePmids(string json)
    {
        return Parse(json).Pmids;
    }

    private static string ReadPmid(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new PubMedResponseException("PubMed ESearch idlist contained a non-string PMID value.");
        }

        var value = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || !PmidPattern.IsMatch(value))
        {
            throw new PubMedResponseException("PubMed ESearch idlist contained an invalid PMID value.");
        }

        return value;
    }

    private static int? ReadCount(JsonElement searchResult)
    {
        if (!searchResult.TryGetProperty("count", out var countElement))
        {
            return null;
        }

        return countElement.ValueKind switch
        {
            JsonValueKind.Number when countElement.TryGetInt32(out var numericCount) => numericCount,
            JsonValueKind.String when int.TryParse(countElement.GetString(), out var stringCount) => stringCount,
            _ => null
        };
    }
}

public sealed record PubMedSearchResult(IReadOnlyList<string> Pmids, int? TotalAvailableCount);
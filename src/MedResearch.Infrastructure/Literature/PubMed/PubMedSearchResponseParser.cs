using System.Net;
using System.Text.Json;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedSearchResponseParser
{
    public IReadOnlyList<string> ParsePmids(string json)
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

            return idList
                .EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new PubMedResponseException("PubMed ESearch response was not valid JSON.", exception);
        }
    }
}

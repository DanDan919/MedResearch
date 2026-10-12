using System.Text.RegularExpressions;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Literature.Identity;

namespace MedResearch.Infrastructure.QuickResearch;

public sealed partial class QuickScientificSources(IEnumerable<IScientificLiteratureSource> sources) : IQuickScientificSources
{
    public const int CandidateLimit = 8;
    private readonly Dictionary<string, IScientificLiteratureSource> _sources = sources
        .Where(s => s.SourceName is ScientificLiteratureSourceNames.PubMed or ScientificLiteratureSourceNames.EuropePmc)
        .ToDictionary(s => s.SourceName, StringComparer.Ordinal);
    public string[] EnabledProviders => _sources.Keys.Order(StringComparer.Ordinal).ToArray();
    public string PrepareQuery(string provider, string query) => _sources[provider].PrepareQuery(query);

    public async Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token)
    {
        var response = await _sources[provider].SearchAsync(new(runId, attemptId, query, CandidateLimit), token);
        if (response.Source != provider || response.ReturnedResultCount is < 0 or > CandidateLimit || response.Candidates.Count > CandidateLimit
            || response.Candidates.Any(c => c.Source != provider)
            || response.ReturnedResultCount == 0 && response.Candidates.Count > 0)
            throw new ScientificLiteratureSourceException("Invalid Quick provider result binding.", LiteratureProviderFailureCategory.InvalidResponse);
        var mapped = new List<QuickCandidate>();
        var rejected = 0;
        foreach (var candidate in response.Candidates.Take(CandidateLimit))
        {
            token.ThrowIfCancellationRequested();
            var pmid = ScientificIdentifierNormalizer.NormalizePmid(candidate.Pmid);
            var pmcid = ScientificIdentifierNormalizer.NormalizePmcid(candidate.Pmcid);
            var doi = ScientificIdentifierNormalizer.NormalizeDoi(candidate.Doi);
            var title = ScientificIdentifierNormalizer.NormalizeWhitespace(candidate.Title);
            if (pmid is not null && !Pmid().IsMatch(pmid) || pmcid is not null && !Pmcid().IsMatch(pmcid)
                || doi is not null && (doi.Length > 300 || !Doi().IsMatch(doi))
                || !string.IsNullOrWhiteSpace(candidate.Pmid) && pmid is null || !string.IsNullOrWhiteSpace(candidate.Pmcid) && pmcid is null
                || pmid is null && pmcid is null && doi is null || title is null || title.Length > 1000
                || candidate.ProviderRecordId is { Length: > 200 } || candidate.Abstract is { Length: > 16000 }
                || candidate.Journal is { Length: > 300 } || candidate.Authors.Count > 50 || candidate.PublicationTypes.Count > 25
                || candidate.PublicationYear is < 1 or > 9999
                || candidate.PublicationDate is not null && candidate.PublicationYear is not null && candidate.PublicationDate.Value.Year != candidate.PublicationYear)
            { rejected++; continue; }
            var abstractText = ScientificIdentifierNormalizer.NormalizeWhitespace(candidate.Abstract);
            var journal = ScientificIdentifierNormalizer.NormalizeWhitespace(candidate.Journal);
            mapped.Add(new(pmid, pmcid, doi, title, abstractText,
                journal, candidate.PublicationDate, candidate.PublicationYear,
                CleanList(candidate.Authors, 50, 300), CleanList(candidate.PublicationTypes, 25, 200), new(attemptId, provider, candidate.ProviderRecordId)));
        }
        return new(response.ReturnedResultCount, mapped.ToArray(), rejected);
    }

    private static string[] CleanList(IEnumerable<string> values, int count, int size) => values
        .Select(ScientificIdentifierNormalizer.NormalizeWhitespace).Where(v => v is not null && v.Length <= size)
        .Select(v => v!).Distinct(StringComparer.Ordinal).Take(count).ToArray();
    [GeneratedRegex("^[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant)] private static partial Regex Pmid();
    [GeneratedRegex("^PMC[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant)] private static partial Regex Pmcid();
    [GeneratedRegex(@"^10\.[0-9]{4,9}/\S+$", RegexOptions.CultureInvariant)] private static partial Regex Doi();
}

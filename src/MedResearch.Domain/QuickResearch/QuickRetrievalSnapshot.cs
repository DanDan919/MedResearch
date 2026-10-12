using System.Text.RegularExpressions;

namespace MedResearch.Domain.QuickResearch;

public enum QuickRetrievalOutcome { Searching, Selected, ZeroResults, NoRelevantSources, Partial, Failed, NeedsClarification }
public enum QuickSearchOutcome { Started, SucceededWithResults, SucceededZeroResults, Failed, TimedOut, OutcomeUnknown, Disabled }
public sealed record QuickQueryTerm(string QueryText, string[] MatchTerms);
public sealed record QuickQueryPlan(string Version, string Language, string[] Queries, QuickQueryTerm[] Terms, string? Population, string[] PopulationTerms);
public sealed record QuickSearchAttempt(Guid Id, Guid QuickRunId, string Provider, string Query, string? ExecutedQuery,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, QuickSearchOutcome Outcome, int? ReturnedCount, string? FailureCategory);
public sealed record QuickDiscovery(Guid AttemptId, string Provider, string? ProviderRecordId);
public sealed record QuickPublication(Guid Id, Guid QuickRunId, string? Pmid, string? Pmcid, string? Doi,
    string Title, string? Abstract, string? Journal, DateOnly? PublicationDate, int? PublicationYear,
    string[] Authors, string[] PublicationTypes, QuickDiscovery[] Discoveries, string[] MetadataConflicts, string PopulationMatch)
{
    public string Url => Pmid is not null ? $"https://pubmed.ncbi.nlm.nih.gov/{Pmid}/"
        : Pmcid is not null ? $"https://europepmc.org/articles/{Pmcid}"
        : $"https://doi.org/{Uri.EscapeDataString(Doi!)}";
}

// Candidates are a bounded recovery buffer, never a clinical-evidence corpus or full text.
public sealed record QuickCandidate(string? Pmid, string? Pmcid, string? Doi, string Title, string? Abstract,
    string? Journal, DateOnly? PublicationDate, int? PublicationYear, string[] Authors, string[] PublicationTypes, QuickDiscovery Discovery);
public sealed record QuickRetrievalSnapshot(Guid QuickRunId, QuickQueryPlan Plan, QuickSearchAttempt[] Attempts,
    QuickCandidate[] Candidates, QuickPublication[] Sources, QuickRetrievalOutcome Outcome, bool IsFinal,
    int RejectedCount, string[] Limitations)
{
    public void Validate(Guid runId)
    {
        if (runId == Guid.Empty || QuickRunId != runId || Plan is null || Plan.Version != "quick-query-v1"
            || Plan.Language is not ("ru" or "en") || Plan.Queries is null || Plan.Queries.Length > 2
            || Plan.Queries.Any(q => !Text(q, 512)) || Plan.Queries.Distinct(StringComparer.Ordinal).Count() != Plan.Queries.Length
            || Plan.Terms is null || Plan.Terms.Length > 12
            || Plan.Terms.Any(t => t is null || !Text(t.QueryText, 100) || t.MatchTerms is null || t.MatchTerms.Length is < 1 or > 5 || t.MatchTerms.Any(m => !Text(m, 100)))
            || Plan.PopulationTerms is null || Plan.PopulationTerms.Length > 5
            || Plan.PopulationTerms.Any(t => !Text(t, 100)) || Plan.Population is { Length: > 100 }
            || Attempts is null || Candidates is null || Sources is null || Limitations is null
            || Attempts.Length > 4 || Candidates.Length > 32 || Sources.Length > 5 || RejectedCount < 0
            || !Enum.IsDefined(Outcome) || IsFinal == (Outcome == QuickRetrievalOutcome.Searching)
            || (IsFinal && Candidates.Length != 0) || Limitations.Length > 10 || Limitations.Any(t => !Text(t, 1000)))
            throw new ArgumentException("Invalid Quick retrieval snapshot.");
        var attempts = new Dictionary<Guid, QuickSearchAttempt>();
        foreach (var attempt in Attempts)
        {
            if (attempt is null || attempt.Id == Guid.Empty || !attempts.TryAdd(attempt.Id, attempt) || attempt.QuickRunId != runId
                || attempt.Provider is not ("PubMed" or "EuropePmc") || !Plan.Queries.Contains(attempt.Query, StringComparer.Ordinal)
                || attempt.ExecutedQuery is { Length: > 512 } || !Enum.IsDefined(attempt.Outcome)
                || (attempt.Outcome == QuickSearchOutcome.Started) != (attempt.FinishedAt is null)
                || attempt.FinishedAt < attempt.StartedAt || attempt.ReturnedCount is < 0 or > 8 || attempt.FailureCategory is { Length: > 64 }
                || attempt.Outcome == QuickSearchOutcome.SucceededZeroResults && attempt.ReturnedCount != 0
                || attempt.Outcome == QuickSearchOutcome.SucceededWithResults && attempt.ReturnedCount is not (> 0)
                || attempt.Outcome is QuickSearchOutcome.SucceededZeroResults or QuickSearchOutcome.SucceededWithResults && attempt.FailureCategory is not null
                || attempt.Outcome == QuickSearchOutcome.Started && (attempt.ReturnedCount is not null || attempt.FailureCategory is not null)
                || attempt.Outcome is QuickSearchOutcome.Failed or QuickSearchOutcome.TimedOut or QuickSearchOutcome.OutcomeUnknown or QuickSearchOutcome.Disabled
                    && (!Text(attempt.FailureCategory, 64) || attempt.ReturnedCount is not null))
                throw new ArgumentException("Invalid Quick search provenance.");
        }
        if (Attempts.GroupBy(a => (a.Provider, a.Query)).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate Quick source execution.");
        bool Discovery(QuickDiscovery? discovery) => discovery is not null && attempts.TryGetValue(discovery.AttemptId, out var attempt)
            && attempt.Provider == discovery.Provider && discovery.ProviderRecordId is not { Length: > 200 }
            && attempt.Outcome is QuickSearchOutcome.SucceededWithResults;
        bool Metadata(string? pmid, string? pmcid, string? doi, string title, string? abstractText, string? journal, string[] authors, string[] types) =>
            ValidIdentifiers(pmid, pmcid, doi) && Text(title, 1000)
            && abstractText is not { Length: > 16000 } && journal is not { Length: > 300 }
            && authors is not null && authors.Length <= 50 && authors.All(a => Text(a, 300))
            && types is not null && types.Length <= 25 && types.All(t => Text(t, 200));
        foreach (var candidate in Candidates)
            if (candidate is null || !Discovery(candidate.Discovery) || !Metadata(candidate.Pmid, candidate.Pmcid, candidate.Doi,
                candidate.Title, candidate.Abstract, candidate.Journal, candidate.Authors, candidate.PublicationTypes)
                || !ValidDate(candidate.PublicationDate, candidate.PublicationYear))
                throw new ArgumentException("Invalid Quick candidate lineage.");
        var sourceIds = new HashSet<Guid>();
        foreach (var source in Sources)
            if (source is null || source.Id == Guid.Empty || !sourceIds.Add(source.Id) || source.QuickRunId != runId
                || !Metadata(source.Pmid, source.Pmcid, source.Doi, source.Title, source.Abstract, source.Journal, source.Authors, source.PublicationTypes)
                || source.Discoveries is null || source.Discoveries.Length is < 1 or > 32 || source.Discoveries.Any(d => !Discovery(d))
                || source.MetadataConflicts is null || source.MetadataConflicts.Length > 10
                || source.MetadataConflicts.Any(c => !Text(c, 100)) || source.PopulationMatch is not ("Unspecified" or "MatchedText"))
                throw new ArgumentException("Invalid Quick publication lineage.");
        if (Sources.Any(s => !ValidDate(s.PublicationDate, s.PublicationYear))) throw new ArgumentException("Invalid Quick publication date.");
        if (IsFinal)
        {
            if (Attempts.Any(a => a.Outcome == QuickSearchOutcome.Started) || Attempts.Length != Plan.Queries.Length * 2)
                throw new ArgumentException("Incomplete Quick execution provenance.");
            var successes = Attempts.Count(a => a.Outcome is QuickSearchOutcome.SucceededWithResults or QuickSearchOutcome.SucceededZeroResults);
            var expected = Plan.Queries.Length == 0 ? QuickRetrievalOutcome.NeedsClarification : successes == 0 ? QuickRetrievalOutcome.Failed
                : successes < Attempts.Length ? QuickRetrievalOutcome.Partial : Sources.Length > 0 ? QuickRetrievalOutcome.Selected
                : Attempts.All(a => a.ReturnedCount == 0) ? QuickRetrievalOutcome.ZeroResults : QuickRetrievalOutcome.NoRelevantSources;
            if (Outcome != expected) throw new ArgumentException("Incoherent Quick coverage outcome.");
        }
        if (Outcome == QuickRetrievalOutcome.Selected && Sources.Length == 0
            || Outcome is QuickRetrievalOutcome.ZeroResults or QuickRetrievalOutcome.Failed or QuickRetrievalOutcome.NeedsClarification && Sources.Length != 0)
            throw new ArgumentException("Incoherent Quick retrieval outcome.");
    }

    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
    private static bool ValidDate(DateOnly? date, int? year) => year is not (< 1 or > 9999) && (date is null || year is null || date.Value.Year == year);
    private static bool ValidIdentifiers(string? pmid, string? pmcid, string? doi) => (pmid is not null || pmcid is not null || doi is not null)
        && (pmid is null || Regex.IsMatch(pmid, "^[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant))
        && (pmcid is null || Regex.IsMatch(pmcid, "^PMC[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant))
        && (doi is null || doi.Length <= 300 && doi == doi.ToLowerInvariant() && Regex.IsMatch(doi, @"^10\.[0-9]{4,9}/\S+$", RegexOptions.CultureInvariant));
}

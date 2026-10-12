using MedResearch.Domain.QuickResearch;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.QuickResearch;

public sealed class QuickRetrievalOptions
{
    public bool Enabled { get; set; }
    public int SearchTimeoutSeconds { get; set; } = 10;
    public void Validate() { if (SearchTimeoutSeconds is < 1 or > 30) throw new InvalidOperationException("Quick retrieval search timeout must be 1..30 seconds."); }
}
public sealed record QuickProviderPage(int ReturnedCount, QuickCandidate[] Candidates, int RejectedCount);
public interface IQuickScientificSources
{
    string[] EnabledProviders { get; }
    string PrepareQuery(string provider, string query);
    Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token);
}
public interface IQuickPublicationSelector
{
    (QuickPublication[] Sources, int RejectedCount) Select(Guid runId, QuickQueryPlan plan, QuickCandidate[] candidates);
}

public sealed class QuickRetrievalProcessor(IQuickResearchStore store, IQuickScientificSources sources,
    IQuickPublicationSelector selector, QuickRetrievalOptions options, ILogger<QuickRetrievalProcessor> logger)
{
    public bool Enabled => options.Enabled;

    public async Task<QuickRetrievalSnapshot> ProcessAsync(QuickResearchClaim claim, CancellationToken token, bool settle = true)
    {
        var snapshot = claim.Retrieval ?? new QuickRetrievalSnapshot(claim.RunId, QuickQueryPreparation.Prepare(claim.Question!),
            [], [], [], QuickRetrievalOutcome.Searching, false, 0, []);
        snapshot.Validate(claim.RunId);
        if (snapshot.Plan.Queries.Length == 0)
        {
            snapshot = snapshot with { Outcome = QuickRetrievalOutcome.NeedsClarification, IsFinal = true,
                Limitations = ["A bounded query could not be prepared without guessing. Rephrase in English or use supported Russian terms."] };
            await Save(snapshot, settle);
            return snapshot;
        }
        // An abandoned Started attempt has an unknown outcome: never spend another call automatically.
        snapshot = snapshot with { Attempts = snapshot.Attempts.Select(a => a.Outcome == QuickSearchOutcome.Started
            ? a with { Outcome = QuickSearchOutcome.OutcomeUnknown, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = "OutcomeUnknown" } : a).ToArray() };
        foreach (var query in snapshot.Plan.Queries)
        foreach (var provider in new[] { "PubMed", "EuropePmc" })
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.Attempts.Any(a => a.Provider == provider && a.Query == query)) continue;
            var attempt = new QuickSearchAttempt(Guid.NewGuid(), claim.RunId, provider, query, null,
                DateTimeOffset.UtcNow, null, QuickSearchOutcome.Started, null, null);
            var available = options.Enabled && sources.EnabledProviders.Contains(provider, StringComparer.Ordinal);
            if (!available) attempt = attempt with { Outcome = QuickSearchOutcome.Disabled, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = "SourceDisabled" };
            else
            {
                try { attempt = attempt with { ExecutedQuery = sources.PrepareQuery(provider, query) }; }
                catch (Exception) { attempt = attempt with { Outcome = QuickSearchOutcome.Failed, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = "QueryPreparationFailed" }; }
            }
            snapshot = snapshot with { Attempts = [..snapshot.Attempts, attempt] };
            await Save(snapshot, false);
            if (attempt.Outcome != QuickSearchOutcome.Started) continue;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.SearchTimeoutSeconds));
            QuickProviderPage? page = null;
            try
            {
                page = await sources.SearchAsync(provider, claim.RunId, attempt.Id, attempt.ExecutedQuery!, timeout.Token);
                attempt = attempt with { Outcome = page.ReturnedCount == 0 ? QuickSearchOutcome.SucceededZeroResults : QuickSearchOutcome.SucceededWithResults,
                    FinishedAt = DateTimeOffset.UtcNow, ReturnedCount = page.ReturnedCount };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { attempt = attempt with { Outcome = QuickSearchOutcome.TimedOut, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = "Timeout" }; }
            catch (Research.Literature.ScientificLiteratureSourceException error)
            { attempt = attempt with { Outcome = QuickSearchOutcome.Failed, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = error.FailureCategory.ToString() }; }
            catch (Exception) { attempt = attempt with { Outcome = QuickSearchOutcome.Failed, FinishedAt = DateTimeOffset.UtcNow, FailureCategory = "UnexpectedFailure" }; }
            snapshot = snapshot with { Attempts = snapshot.Attempts.Select(a => a.Id == attempt.Id ? attempt : a).ToArray(),
                Candidates = [..snapshot.Candidates, ..(page?.Candidates ?? [])], RejectedCount = snapshot.RejectedCount + (page?.RejectedCount ?? 0) };
            await Save(snapshot, false);
            logger.LogInformation("QuickSearchFinished. QuickRunId: {QuickRunId}; AttemptId: {AttemptId}; Provider: {Provider}; Outcome: {Outcome}; ReturnedCount: {ReturnedCount}",
                claim.RunId, attempt.Id, provider, attempt.Outcome, attempt.ReturnedCount);
        }
        var selection = selector.Select(claim.RunId, snapshot.Plan, snapshot.Candidates);
        var successful = snapshot.Attempts.Count(a => a.Outcome is QuickSearchOutcome.SucceededWithResults or QuickSearchOutcome.SucceededZeroResults);
        var failures = snapshot.Attempts.Length - successful;
        var outcome = successful == 0 ? QuickRetrievalOutcome.Failed : failures > 0 ? QuickRetrievalOutcome.Partial
            : selection.Sources.Length > 0 ? QuickRetrievalOutcome.Selected : snapshot.Attempts.All(a => a.ReturnedCount == 0)
                ? QuickRetrievalOutcome.ZeroResults : QuickRetrievalOutcome.NoRelevantSources;
        snapshot = snapshot with { Candidates = [], Sources = selection.Sources, IsFinal = true, Outcome = outcome,
            RejectedCount = snapshot.RejectedCount + selection.RejectedCount,
            Limitations = ["Publication selection uses lexical topic/population matching, not clinical evidence evaluation.",
                "Source-reported identifiers are normalized and consistency-checked, not independently verified against an identifier registry.",
                "This is a bounded search, not an exhaustive review or medical answer. Missing abstracts remain missing."] };
        await Save(snapshot, settle);
        return snapshot;

        async Task Save(QuickRetrievalSnapshot value, bool settle)
        {
            value.Validate(claim.RunId);
            if (!await store.SaveRetrievalAsync(claim, value, settle, token)) throw new QuickResearchLeaseLostException();
        }
    }
}
public sealed class QuickResearchLeaseLostException : Exception;

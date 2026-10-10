using System.Diagnostics;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Literature;

public sealed class ScientificLiteratureSearchCoordinator : IScientificLiteratureSearchCoordinator
{
    private readonly IReadOnlyCollection<IScientificLiteratureSource> _sources;
    private readonly IScientificSearchResultStore _searchResultStore;
    private readonly ILogger<ScientificLiteratureSearchCoordinator> _logger;

    public ScientificLiteratureSearchCoordinator(
        IEnumerable<IScientificLiteratureSource> sources,
        IScientificSearchResultStore searchResultStore,
        ILogger<ScientificLiteratureSearchCoordinator> logger)
    {
        _sources = sources.ToArray();
        _searchResultStore = searchResultStore;
        _logger = logger;
    }

    public async Task SearchAsync(
        Guid researchRunId,
        Guid researchPlanId,
        IReadOnlyCollection<string> queries,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (researchPlanId == Guid.Empty)
        {
            throw new ArgumentException("Research plan id cannot be empty.", nameof(researchPlanId));
        }

        if (_sources.Count == 0)
        {
            throw new ScientificLiteratureSourceException("No scientific literature sources are enabled.");
        }

        _logger.LogInformation(
            "ScientificSearchSourcesSelected. ResearchRunId: {ResearchRunId}; Sources: {Sources}; SourceCount: {SourceCount}",
            researchRunId, string.Join(",", _sources.Select(source => source.SourceName)), _sources.Count);

        foreach (var query in queries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
            await SearchQueryAsync(researchRunId, researchPlanId, query, cancellationToken);
        }
    }

    private async Task SearchQueryAsync(
        Guid researchRunId,
        Guid researchPlanId,
        string query,
        CancellationToken cancellationToken)
    {
        var successfulSources = 0;
        var failures = new List<Exception>();

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _searchResultStore.HasPersistedSearchAsync(
                    researchRunId,
                    researchPlanId,
                    source.SourceName,
                    query,
                    cancellationToken))
            {
                successfulSources++;
                _logger.LogInformation(
                    "ScientificSearchReused. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; Query: {Query}",
                    researchRunId,
                    researchPlanId,
                    source.SourceName,
                    query);
                continue;
            }

            var searchExecutionId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();
            string executionQuery;
            try
            {
                executionQuery = source.PrepareQuery(query);
            }
            catch (ScientificLiteratureSourceException exception)
            {
                await _searchResultStore.BeginAttemptAsync(searchExecutionId, researchRunId, researchPlanId, source.SourceName, query, DateTimeOffset.UtcNow, cancellationToken);
                await _searchResultStore.FailAttemptAsync(searchExecutionId, exception.FailureCategory, DateTimeOffset.UtcNow, cancellationToken);
                failures.Add(exception);
                _logger.LogWarning(
                    "ScientificSearchQueryRejected. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; FailureCategory: {FailureCategory}",
                    researchRunId, source.SourceName, searchExecutionId, exception.FailureCategory);
                continue;
            }

            // Keep old successful execution keys intact; new executions use the actual provider query.
            if (executionQuery != query && await _searchResultStore.HasPersistedSearchAsync(
                    researchRunId, researchPlanId, source.SourceName, executionQuery, cancellationToken))
            {
                successfulSources++;
                _logger.LogInformation("ScientificSearchReused. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; Query: {Query}",
                    researchRunId, researchPlanId, source.SourceName, executionQuery);
                continue;
            }

            await _searchResultStore.BeginAttemptAsync(searchExecutionId, researchRunId, researchPlanId, source.SourceName, executionQuery, DateTimeOffset.UtcNow, cancellationToken);

            if (executionQuery != query)
                _logger.LogInformation("ScientificSearchQueryAdapted. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}",
                    researchRunId, source.SourceName, searchExecutionId);

            _logger.LogInformation(
                "ScientificSearchStarted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}",
                researchRunId,
                researchPlanId,
                source.SourceName,
                searchExecutionId);

            ScientificSearchResult searchResult;
            try
            {
                searchResult = await source.SearchAsync(
                    new ScientificSearchRequest(researchRunId, searchExecutionId, executionQuery),
                    cancellationToken);
                if (searchResult.Source != source.SourceName || searchResult.ReturnedResultCount < 0)
                    throw new ScientificLiteratureSourceException("Scientific source returned an inconsistent result.", LiteratureProviderFailureCategory.InvalidResponse);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host cancellation must not become scientific failure. A bounded best-effort write is still fenced.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _searchResultStore.FailAttemptAsync(searchExecutionId, LiteratureProviderFailureCategory.Cancelled, DateTimeOffset.UtcNow, cleanup.Token);
                }
                catch (Exception cleanupFailure)
                {
                    _logger.LogWarning("ScientificSearchCancellationNotPersisted. SearchExecutionId: {SearchExecutionId}; FailureType: {FailureType}", searchExecutionId, cleanupFailure.GetType().Name);
                }
                throw;
            }
            catch (Exception exception)
            {
                var category = exception switch
                {
                    ScientificLiteratureSourceException providerFailure => providerFailure.FailureCategory,
                    TimeoutException or OperationCanceledException => LiteratureProviderFailureCategory.Timeout,
                    _ => LiteratureProviderFailureCategory.UnexpectedFailure
                };
                await _searchResultStore.FailAttemptAsync(searchExecutionId, category, DateTimeOffset.UtcNow, cancellationToken);
                failures.Add(exception);
                _logger.LogWarning("ScientificSearchFailed. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; FailureCategory: {FailureCategory}; DurationMs: {DurationMs}",
                    researchRunId, source.SourceName, searchExecutionId, category, stopwatch.ElapsedMilliseconds);
                continue;
            }

            foreach (var candidate in searchResult.Candidates)
            {
                _logger.LogInformation(
                    "ScientificStudyDiscovered. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; PMID: {Pmid}; PMCID: {Pmcid}; DOI: {Doi}; ProviderRecordId: {ProviderRecordId}",
                    researchRunId,
                    researchPlanId,
                    candidate.Source,
                    searchExecutionId,
                    candidate.Pmid,
                    candidate.Pmcid,
                    candidate.Doi,
                    candidate.ProviderRecordId);
            }

            var persistenceResult = await _searchResultStore.PersistSearchResultsAsync(
                new ScientificSearchPersistenceRequest(
                    searchExecutionId,
                    researchRunId,
                    researchPlanId,
                    searchResult.Source,
                    executionQuery,
                    searchResult.SearchedAt,
                    searchResult.ReturnedResultCount,
                    searchResult.Candidates),
                cancellationToken);

            stopwatch.Stop();
            successfulSources++;

            _logger.LogInformation(
                "ScientificSearchCompleted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; ResultCount: {ResultCount}; PersistedCount: {PersistedCount}; DuplicateCount: {DuplicateCount}; DurationMs: {DurationMs}",
                researchRunId,
                researchPlanId,
                searchResult.Source,
                searchExecutionId,
                searchResult.ReturnedResultCount,
                persistenceResult.PersistedCount,
                persistenceResult.DuplicateCount,
                stopwatch.ElapsedMilliseconds);
        }

        if (successfulSources == 0)
        {
            var innerException = failures.FirstOrDefault();
            if (innerException is null)
            {
                throw new ScientificLiteratureSourceException("All enabled scientific literature sources failed for a planned search query.");
            }

            throw new ScientificLiteratureSourceException("All enabled scientific literature sources failed for a planned search query.", innerException);
        }
    }
}

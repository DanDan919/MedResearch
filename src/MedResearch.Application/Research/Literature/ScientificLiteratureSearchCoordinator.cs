using System.Diagnostics;
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
            var searchExecutionId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "ScientificSearchStarted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}",
                researchRunId,
                researchPlanId,
                source.SourceName,
                searchExecutionId);

            try
            {
                var searchResult = await source.SearchAsync(
                    new ScientificSearchRequest(researchRunId, searchExecutionId, query),
                    cancellationToken);

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
                        query,
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                failures.Add(exception);

                _logger.LogError(
                    exception,
                    "ScientificSearchFailed. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; DurationMs: {DurationMs}",
                    researchRunId,
                    researchPlanId,
                    source.SourceName,
                    searchExecutionId,
                    stopwatch.ElapsedMilliseconds);
            }
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

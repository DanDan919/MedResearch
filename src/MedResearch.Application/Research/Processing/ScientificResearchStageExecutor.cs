using System.Diagnostics;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Processing;

public sealed class ScientificResearchStageExecutor : IResearchStageExecutor
{
    private readonly IScientificSearchQueryBuilder _queryBuilder;
    private readonly IScientificLiteratureSource _literatureSource;
    private readonly IScientificSearchResultStore _searchResultStore;
    private readonly ILogger<ScientificResearchStageExecutor> _logger;

    public ScientificResearchStageExecutor(
        IScientificSearchQueryBuilder queryBuilder,
        IScientificLiteratureSource literatureSource,
        IScientificSearchResultStore searchResultStore,
        ILogger<ScientificResearchStageExecutor> logger)
    {
        _queryBuilder = queryBuilder;
        _literatureSource = literatureSource;
        _searchResultStore = searchResultStore;
        _logger = logger;
    }

    public async Task ExecuteAsync(ResearchStageExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Stage != ResearchRunStatus.Searching)
        {
            return;
        }

        var searchExecutionId = Guid.NewGuid();
        var query = _queryBuilder.BuildQuery(context.ResearchQuestion);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ScientificSearchStarted. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}",
            context.ResearchRunId,
            _literatureSource.SourceName,
            searchExecutionId);

        try
        {
            var searchResult = await _literatureSource.SearchAsync(
                new ScientificSearchRequest(context.ResearchRunId, searchExecutionId, query),
                cancellationToken);

            foreach (var candidate in searchResult.Candidates)
            {
                _logger.LogInformation(
                    "ScientificStudyDiscovered. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; PMID: {Pmid}; DOI: {Doi}",
                    context.ResearchRunId,
                    candidate.Source,
                    searchExecutionId,
                    candidate.Pmid,
                    candidate.Doi);
            }

            var persistenceResult = await _searchResultStore.PersistSearchResultsAsync(
                new ScientificSearchPersistenceRequest(
                    searchExecutionId,
                    context.ResearchRunId,
                    searchResult.Source,
                    query,
                    searchResult.SearchedAt,
                    searchResult.ReturnedResultCount,
                    searchResult.Candidates),
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "ScientificStudiesPersisted. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; PersistedCount: {PersistedCount}; DuplicateCount: {DuplicateCount}",
                context.ResearchRunId,
                searchResult.Source,
                searchExecutionId,
                persistenceResult.PersistedCount,
                persistenceResult.DuplicateCount);

            _logger.LogInformation(
                "ScientificSearchCompleted. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; ResultCount: {ResultCount}; PersistedCount: {PersistedCount}; DuplicateCount: {DuplicateCount}; DurationMs: {DurationMs}",
                context.ResearchRunId,
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

            _logger.LogError(
                exception,
                "ScientificSearchFailed. ResearchRunId: {ResearchRunId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                _literatureSource.SourceName,
                searchExecutionId,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}

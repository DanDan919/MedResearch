using System.Diagnostics;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Processing;

public sealed class ScientificResearchStageExecutor : IResearchStageExecutor
{
    private readonly IResearchPlanner _researchPlanner;
    private readonly IResearchPlanStore _researchPlanStore;
    private readonly IScientificLiteratureSource _literatureSource;
    private readonly IScientificSearchResultStore _searchResultStore;
    private readonly ILogger<ScientificResearchStageExecutor> _logger;

    public ScientificResearchStageExecutor(
        IResearchPlanner researchPlanner,
        IResearchPlanStore researchPlanStore,
        IScientificLiteratureSource literatureSource,
        IScientificSearchResultStore searchResultStore,
        ILogger<ScientificResearchStageExecutor> logger)
    {
        _researchPlanner = researchPlanner;
        _researchPlanStore = researchPlanStore;
        _literatureSource = literatureSource;
        _searchResultStore = searchResultStore;
        _logger = logger;
    }

    public async Task ExecuteAsync(ResearchStageExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Stage == ResearchRunStatus.Planning)
        {
            await _researchPlanner.GenerateAndPersistPlanAsync(
                context.ResearchRunId,
                context.ResearchQuestionId,
                context.ResearchQuestion,
                cancellationToken);
            return;
        }

        if (context.Stage != ResearchRunStatus.Searching)
        {
            return;
        }

        var plan = await _researchPlanStore.FindByResearchRunIdAsync(context.ResearchRunId, cancellationToken);
        if (plan is null)
        {
            throw new ResearchPlanValidationException("No accepted research plan exists for the research run.");
        }

        foreach (var query in plan.SearchQueries)
        {
            await ExecuteSearchQueryAsync(context, plan, query, cancellationToken);
        }
    }

    private async Task ExecuteSearchQueryAsync(
        ResearchStageExecutionContext context,
        ResearchPlan plan,
        string query,
        CancellationToken cancellationToken)
    {
        var searchExecutionId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ScientificSearchStarted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}",
            context.ResearchRunId,
            plan.Id,
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
                    "ScientificStudyDiscovered. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; PMID: {Pmid}; DOI: {Doi}",
                    context.ResearchRunId,
                    plan.Id,
                    candidate.Source,
                    searchExecutionId,
                    candidate.Pmid,
                    candidate.Doi);
            }

            var persistenceResult = await _searchResultStore.PersistSearchResultsAsync(
                new ScientificSearchPersistenceRequest(
                    searchExecutionId,
                    context.ResearchRunId,
                    plan.Id,
                    searchResult.Source,
                    query,
                    searchResult.SearchedAt,
                    searchResult.ReturnedResultCount,
                    searchResult.Candidates),
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "ScientificStudiesPersisted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; PersistedCount: {PersistedCount}; DuplicateCount: {DuplicateCount}",
                context.ResearchRunId,
                plan.Id,
                searchResult.Source,
                searchExecutionId,
                persistenceResult.PersistedCount,
                persistenceResult.DuplicateCount);

            _logger.LogInformation(
                "ScientificSearchCompleted. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; ResultCount: {ResultCount}; PersistedCount: {PersistedCount}; DuplicateCount: {DuplicateCount}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                plan.Id,
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
                "ScientificSearchFailed. ResearchRunId: {ResearchRunId}; ResearchPlanId: {ResearchPlanId}; Source: {Source}; SearchExecutionId: {SearchExecutionId}; DurationMs: {DurationMs}",
                context.ResearchRunId,
                plan.Id,
                _literatureSource.SourceName,
                searchExecutionId,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}

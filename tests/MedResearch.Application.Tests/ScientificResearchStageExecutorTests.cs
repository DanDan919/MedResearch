using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ScientificResearchStageExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PlanningStageInvokesPlannerAndPersistsPlan()
    {
        var plan = CreatePlan(["planned query"]);
        var planner = new RecordingResearchPlanner(plan);
        var planStore = new RecordingResearchPlanStore(plan);
        var executor = CreateExecutor(planner, planStore, new RecordingScientificSource([]), new RecordingSearchResultStore());
        var context = CreateContext(ResearchRunStatus.Planning, "Does sleep deprivation impair memory?");

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(context.ResearchRunId, planner.ResearchRunId);
        Assert.Equal(context.ResearchQuestionId, planner.ResearchQuestionId);
        Assert.Equal("Does sleep deprivation impair memory?", planner.ResearchQuestion);
        Assert.Empty(planStore.FindRequests);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageUsesResearchPlanQueriesInsteadOfResearchQuestionText()
    {
        var plan = CreatePlan(["planned sleep query"]);
        var source = new RecordingScientificSource([
            CreateCandidate("12345678", "10.1000/example")
        ]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);
        var context = CreateContext(ResearchRunStatus.Searching, "raw question should not become the PubMed query");

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(source.Requests);
        Assert.Equal("planned sleep query", source.Requests[0].Query);
        Assert.Single(store.Requests);
        Assert.Equal(plan.Id, store.Requests[0].ResearchPlanId);
        Assert.Equal("planned sleep query", store.Requests[0].Query);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageExecutesMultiplePlanQueriesSequentially()
    {
        var plan = CreatePlan(["query one", "query two"]);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None);

        Assert.Equal(["query one", "query two"], source.Requests.Select(request => request.Query).ToArray());
        Assert.Equal(["query one", "query two"], store.Requests.Select(request => request.Query).ToArray());
        Assert.All(store.Requests, request => Assert.Equal(plan.Id, request.ResearchPlanId));
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStagePersistsZeroResultSearches()
    {
        var plan = CreatePlan(["rare planned topic"]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(
            new RecordingResearchPlanner(plan),
            new RecordingResearchPlanStore(plan),
            new RecordingScientificSource([]),
            store);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None);

        Assert.Single(store.Requests);
        Assert.Empty(store.Requests[0].Candidates);
        Assert.Equal(0, store.Requests[0].ResultCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonPlanningAndNonSearchingStageDoesNotInvokePlannerOrSource()
    {
        var plan = CreatePlan(["planned query"]);
        var planner = new RecordingResearchPlanner(plan);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(planner, new RecordingResearchPlanStore(plan), source, store);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Extracting), CancellationToken.None);

        Assert.Null(planner.ResearchQuestion);
        Assert.Empty(source.Requests);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageFailsSafelyWhenPlanIsMissing()
    {
        var executor = CreateExecutor(
            new RecordingResearchPlanner(CreatePlan(["planned query"])),
            new RecordingResearchPlanStore(null),
            new RecordingScientificSource([]),
            new RecordingSearchResultStore());

        await Assert.ThrowsAsync<ResearchPlanValidationException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SourceFailurePropagatesToProcessorFailurePath()
    {
        var plan = CreatePlan(["planned query"]);
        var source = new RecordingScientificSource([], new ScientificLiteratureSourceException("PubMed request failed."));
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, new RecordingSearchResultStore());

        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagatesWithoutPersistingResults()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var plan = CreatePlan(["planned query"]);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), cancellation.Token));

        Assert.Empty(source.Requests);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task ProcessNextQueuedRunAsync_PlanningFailureMarksRunFailed()
    {
        var run = new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
        run.StartPlanning(DateTimeOffset.UtcNow);
        var queue = new ProcessorSearchQueue(new ClaimedResearchRun(run, "Does planning failure mark the run failed?"));
        var planner = new RecordingResearchPlanner(CreatePlan(["planned query"]), new ResearchPlanValidationException("invalid plan"));
        var executor = CreateExecutor(planner, new RecordingResearchPlanStore(null), new RecordingScientificSource([]), new RecordingSearchResultStore());
        var processor = new ResearchRunProcessor(queue, executor, NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal("Research processing failed.", run.FailureReason);
        Assert.True(queue.MarkFailedWasCalled);
    }

    private static ResearchStageExecutionContext CreateContext(
        ResearchRunStatus stage,
        string question = "Does chronic sleep deprivation impair working memory in adults?")
    {
        return new ResearchStageExecutionContext(Guid.NewGuid(), Guid.NewGuid(), stage, question, "worker-1");
    }

    private static ScientificResearchStageExecutor CreateExecutor(
        IResearchPlanner planner,
        IResearchPlanStore planStore,
        IScientificLiteratureSource source,
        IScientificSearchResultStore store)
    {
        return new ScientificResearchStageExecutor(
            planner,
            planStore,
            source,
            store,
            NullLogger<ScientificResearchStageExecutor>.Instance);
    }

    private static ResearchPlan CreatePlan(string[] searchQueries)
    {
        return new ResearchPlan(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does chronic sleep deprivation impair working memory in adults?",
            "adults",
            "chronic sleep deprivation",
            "normal sleep",
            ["working memory"],
            ["observational study"],
            searchQueries,
            [],
            "FakeLLM",
            "fake-model",
            ResearchPlannerPrompt.Version,
            DateTimeOffset.UtcNow);
    }

    private static ScientificStudyCandidate CreateCandidate(string pmid, string doi)
    {
        return new ScientificStudyCandidate(
            pmid,
            doi,
            "Sleep and memory",
            null,
            "Journal",
            null,
            2024,
            null,
            null,
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
    }

    private sealed class RecordingResearchPlanner : IResearchPlanner
    {
        private readonly ResearchPlan _plan;
        private readonly Exception? _exception;

        public RecordingResearchPlanner(ResearchPlan plan, Exception? exception = null)
        {
            _plan = plan;
            _exception = exception;
        }

        public Guid? ResearchRunId { get; private set; }

        public Guid? ResearchQuestionId { get; private set; }

        public string? ResearchQuestion { get; private set; }

        public Task<ResearchPlan> GenerateAndPersistPlanAsync(
            Guid researchRunId,
            Guid researchQuestionId,
            string researchQuestion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResearchRunId = researchRunId;
            ResearchQuestionId = researchQuestionId;
            ResearchQuestion = researchQuestion;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_plan);
        }
    }

    private sealed class RecordingResearchPlanStore : IResearchPlanStore
    {
        private readonly ResearchPlan? _plan;

        public RecordingResearchPlanStore(ResearchPlan? plan)
        {
            _plan = plan;
        }

        public List<Guid> FindRequests { get; } = [];

        public Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            FindRequests.Add(researchRunId);
            return Task.FromResult(_plan);
        }
    }

    private sealed class ProcessorSearchQueue : IResearchRunQueue
    {
        private readonly ClaimedResearchRun _claimedRun;

        public ProcessorSearchQueue(ClaimedResearchRun claimedRun)
        {
            _claimedRun = claimedRun;
        }

        public bool MarkFailedWasCalled { get; private set; }

        public Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            return Task.FromResult<ClaimedResearchRun?>(_claimedRun);
        }

        public Task SaveProgressAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> MarkFailedAsync(Guid researchRunId, string safeFailureReason, DateTimeOffset failedAt, CancellationToken cancellationToken)
        {
            MarkFailedWasCalled = true;
            _claimedRun.Run.Fail(safeFailureReason, failedAt);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingScientificSource : IScientificLiteratureSource
    {
        private readonly IReadOnlyCollection<ScientificStudyCandidate> _candidates;
        private readonly Exception? _exception;

        public RecordingScientificSource(IReadOnlyCollection<ScientificStudyCandidate> candidates, Exception? exception = null)
        {
            _candidates = candidates;
            _exception = exception;
        }

        public string SourceName => "PubMed";

        public List<ScientificSearchRequest> Requests { get; } = [];

        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new ScientificSearchResult(SourceName, DateTimeOffset.UtcNow, _candidates.Count, _candidates));
        }
    }

    private sealed class RecordingSearchResultStore : IScientificSearchResultStore
    {
        public List<ScientificSearchPersistenceRequest> Requests { get; } = [];

        public Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
            ScientificSearchPersistenceRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ScientificSearchPersistenceResult(request.SearchExecutionId, request.Candidates.Count, 0));
        }
    }
}

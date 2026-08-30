using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ScientificResearchStageExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SearchingStageInvokesScientificSourceWithResearchQuestionContext()
    {
        var source = new RecordingScientificSource([
            CreateCandidate("12345678", "10.1000/example")
        ]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(source, store);
        var context = new ResearchStageExecutionContext(
            Guid.NewGuid(),
            ResearchRunStatus.Searching,
            "  Does sleep deprivation impair memory?  ",
            "worker-1");

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(source.Request);
        Assert.Equal(context.ResearchRunId, source.Request.ResearchRunId);
        Assert.Equal("Does sleep deprivation impair memory?", source.Request.Query);
        Assert.NotEqual(Guid.Empty, source.Request.SearchExecutionId);
        Assert.NotNull(store.Request);
        Assert.Equal(source.Request.SearchExecutionId, store.Request.SearchExecutionId);
        Assert.Equal(context.ResearchRunId, store.Request.ResearchRunId);
        Assert.Equal("PubMed", store.Request.Source);
        Assert.Equal("Does sleep deprivation impair memory?", store.Request.Query);
        Assert.Single(store.Request.Candidates);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStagePersistsEmptyResultSet()
    {
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(source, store);

        await executor.ExecuteAsync(
            new ResearchStageExecutionContext(Guid.NewGuid(), ResearchRunStatus.Searching, "rare topic", "worker-1"),
            CancellationToken.None);

        Assert.NotNull(store.Request);
        Assert.Empty(store.Request.Candidates);
        Assert.Equal(0, store.Request.ResultCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonSearchingStageDoesNotInvokeScientificSource()
    {
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(source, store);

        await executor.ExecuteAsync(
            new ResearchStageExecutionContext(Guid.NewGuid(), ResearchRunStatus.Planning, "question", "worker-1"),
            CancellationToken.None);

        Assert.Null(source.Request);
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task ExecuteAsync_SourceFailurePropagatesToProcessorFailurePath()
    {
        var source = new RecordingScientificSource([], new ScientificLiteratureSourceException("PubMed request failed."));
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(source, store);

        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => executor.ExecuteAsync(
            new ResearchStageExecutionContext(Guid.NewGuid(), ResearchRunStatus.Searching, "question", "worker-1"),
            CancellationToken.None));

        Assert.Null(store.Request);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagatesWithoutPersistingResults()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(source, store);

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new ResearchStageExecutionContext(Guid.NewGuid(), ResearchRunStatus.Searching, "question", "worker-1"),
            cancellation.Token));

        Assert.Null(source.Request);
        Assert.Null(store.Request);
    }


    [Fact]
    public async Task ProcessNextQueuedRunAsync_SourceFailureDuringSearchingMarksRunFailed()
    {
        var run = new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
        run.StartPlanning(DateTimeOffset.UtcNow);
        var queue = new ProcessorSearchQueue(new ClaimedResearchRun(run, "Does source failure mark the run failed?"));
        var source = new RecordingScientificSource([], new ScientificLiteratureSourceException("PubMed request failed."));
        var executor = CreateExecutor(source, new RecordingSearchResultStore());
        var processor = new ResearchRunProcessor(
            queue,
            executor,
            NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal("Research processing failed.", run.FailureReason);
        Assert.True(queue.MarkFailedWasCalled);
    }
    private static ScientificResearchStageExecutor CreateExecutor(
        IScientificLiteratureSource source,
        IScientificSearchResultStore store)
    {
        return new ScientificResearchStageExecutor(
            new DeterministicScientificSearchQueryBuilder(),
            source,
            store,
            NullLogger<ScientificResearchStageExecutor>.Instance);
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

        public ScientificSearchRequest? Request { get; private set; }

        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new ScientificSearchResult(SourceName, DateTimeOffset.UtcNow, _candidates.Count, _candidates));
        }
    }

    private sealed class RecordingSearchResultStore : IScientificSearchResultStore
    {
        public ScientificSearchPersistenceRequest? Request { get; private set; }

        public Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
            ScientificSearchPersistenceRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ScientificSearchPersistenceResult(request.SearchExecutionId, request.Candidates.Count, 0));
        }
    }
}


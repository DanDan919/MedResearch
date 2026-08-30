using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ResearchRunProcessorTests
{
    [Fact]
    public async Task ProcessNextQueuedRunAsync_AdvancesClaimedRunThroughLifecycle()
    {
        var run = CreateClaimedRun();
        var queue = new RecordingResearchRunQueue(new ClaimedResearchRun(run, "Does sleep improve memory?"));
        var stageExecutor = new RecordingStageExecutor();
        var processor = new ResearchRunProcessor(
            queue,
            stageExecutor,
            NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(ResearchRunStatus.Completed, run.Status);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.CompletedAt);
        Assert.Null(run.FailureReason);
        Assert.All(stageExecutor.Contexts, context =>
        {
            Assert.Equal(run.Id, context.ResearchRunId);
            Assert.Equal("Does sleep improve memory?", context.ResearchQuestion);
            Assert.Equal("worker-1", context.WorkerInstanceId);
        });
        Assert.Equal(
            [
                ResearchRunStatus.Planning,
                ResearchRunStatus.Searching,
                ResearchRunStatus.Extracting,
                ResearchRunStatus.Evaluating,
                ResearchRunStatus.Synthesizing
            ],
            stageExecutor.Contexts.Select(context => context.Stage).ToArray());
        Assert.Equal(
            [
                ResearchRunStatus.Searching,
                ResearchRunStatus.Extracting,
                ResearchRunStatus.Evaluating,
                ResearchRunStatus.Synthesizing,
                ResearchRunStatus.Completed
            ],
            queue.SavedStatuses);
        Assert.False(queue.MarkFailedWasCalled);
    }

    [Fact]
    public async Task ProcessNextQueuedRunAsync_ReturnsFalseWhenNoQueuedRunExists()
    {
        var queue = new RecordingResearchRunQueue(null);
        var processor = new ResearchRunProcessor(
            queue,
            new RecordingStageExecutor(),
            NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.False(processed);
        Assert.Empty(queue.SavedStatuses);
        Assert.False(queue.MarkFailedWasCalled);
    }

    [Fact]
    public async Task ProcessNextQueuedRunAsync_MarksRunFailedWhenStageFails()
    {
        var run = CreateClaimedRun();
        var queue = new RecordingResearchRunQueue(new ClaimedResearchRun(run, "Does sleep improve memory?"));
        var stageExecutor = new RecordingStageExecutor(ResearchRunStatus.Extracting);
        var processor = new ResearchRunProcessor(
            queue,
            stageExecutor,
            NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal("Research processing failed.", run.FailureReason);
        Assert.NotNull(run.CompletedAt);
        Assert.True(queue.MarkFailedWasCalled);
    }

    [Fact]
    public async Task ProcessNextQueuedRunAsync_DoesNotMarkFailureWhenHostShutdownIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var run = CreateClaimedRun();
        var queue = new RecordingResearchRunQueue(new ClaimedResearchRun(run, "Does sleep improve memory?"));
        var stageExecutor = new CancellingStageExecutor(cancellation);
        var processor = new ResearchRunProcessor(
            queue,
            stageExecutor,
            NullLogger<ResearchRunProcessor>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            processor.ProcessNextQueuedRunAsync("worker-1", cancellation.Token));

        Assert.Equal(ResearchRunStatus.Planning, run.Status);
        Assert.Null(run.FailureReason);
        Assert.False(queue.MarkFailedWasCalled);
    }

    private static ResearchRun CreateClaimedRun()
    {
        var run = new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
        run.StartPlanning(DateTimeOffset.UtcNow);
        return run;
    }

    private sealed class RecordingResearchRunQueue : IResearchRunQueue
    {
        private readonly ClaimedResearchRun? _claimedRun;

        public RecordingResearchRunQueue(ClaimedResearchRun? claimedRun)
        {
            _claimedRun = claimedRun;
        }

        public List<ResearchRunStatus> SavedStatuses { get; } = [];

        public bool MarkFailedWasCalled { get; private set; }

        public Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            return Task.FromResult(_claimedRun);
        }

        public Task SaveProgressAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken)
        {
            SavedStatuses.Add(claimedRun.Run.Status);
            return Task.CompletedTask;
        }

        public Task<bool> MarkFailedAsync(
            Guid researchRunId,
            string safeFailureReason,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            MarkFailedWasCalled = true;
            _claimedRun?.Run.Fail(safeFailureReason, failedAt);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingStageExecutor : IResearchStageExecutor
    {
        private readonly ResearchRunStatus? _stageToFail;

        public RecordingStageExecutor(ResearchRunStatus? stageToFail = null)
        {
            _stageToFail = stageToFail;
        }

        public List<ResearchStageExecutionContext> Contexts { get; } = [];

        public Task ExecuteAsync(ResearchStageExecutionContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);

            if (context.Stage == _stageToFail)
            {
                throw new InvalidOperationException("Synthetic stage failure for processor testing.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancellingStageExecutor : IResearchStageExecutor
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingStageExecutor(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public Task ExecuteAsync(ResearchStageExecutionContext context, CancellationToken cancellationToken)
        {
            _cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

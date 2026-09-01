using System.Diagnostics;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Processing;

public sealed class ResearchRunProcessor
{
    private const string SafeFailureReason = "Research processing failed.";

    private readonly IResearchRunQueue _researchRunQueue;
    private readonly IResearchStageExecutor _stageExecutor;
    private readonly ILogger<ResearchRunProcessor> _logger;

    public ResearchRunProcessor(
        IResearchRunQueue researchRunQueue,
        IResearchStageExecutor stageExecutor,
        ILogger<ResearchRunProcessor> logger)
    {
        _researchRunQueue = researchRunQueue;
        _stageExecutor = stageExecutor;
        _logger = logger;
    }

    public async Task<bool> ProcessNextQueuedRunAsync(
        string workerInstanceId,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerInstanceId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        if (heartbeatInterval <= TimeSpan.Zero || heartbeatInterval >= leaseDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), "Heartbeat interval must be positive and shorter than the lease duration.");
        }

        _logger.LogInformation(
            "ResearchRunClaimAttempt. WorkerId: {WorkerId}; LeaseDurationMs: {LeaseDurationMs}; HeartbeatIntervalMs: {HeartbeatIntervalMs}",
            workerInstanceId,
            leaseDuration.TotalMilliseconds,
            heartbeatInterval.TotalMilliseconds);

        var claimedAt = DateTimeOffset.UtcNow;
        var claimedRun = await _researchRunQueue.TryClaimNextQueuedRunAsync(
            claimedAt,
            workerInstanceId,
            leaseDuration,
            cancellationToken);

        if (claimedRun is null)
        {
            return false;
        }

        var run = claimedRun.Run;

        _logger.LogInformation(
            claimedRun.WasReclaimed
                ? "ResearchRunReclaimed. ResearchRunId: {ResearchRunId}; Stage: {Stage}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}; LeaseExpiresAt: {LeaseExpiresAt}"
                : "ResearchRunClaimed. ResearchRunId: {ResearchRunId}; Stage: {Stage}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}; LeaseExpiresAt: {LeaseExpiresAt}",
            run.Id,
            run.Status,
            workerInstanceId,
            claimedRun.LeaseVersion,
            claimedRun.LeaseExpiresAt);

        try
        {
            while (run.Status is ResearchRunStatus.Planning or ResearchRunStatus.Searching or ResearchRunStatus.Extracting or ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing)
            {
                await ExecuteCurrentStageAsync(claimedRun, workerInstanceId, leaseDuration, heartbeatInterval, cancellationToken);
                AdvanceAfterCurrentStage(run);
                await SaveProgressOrThrowAsync(claimedRun, leaseDuration, cancellationToken);
            }

            if (run.Status == ResearchRunStatus.Completed)
            {
                _logger.LogInformation(
                    "ResearchRunCompleted. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                    run.Id,
                    run.Status,
                    workerInstanceId,
                    claimedRun.LeaseVersion);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Research run processing cancelled because host shutdown was requested. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                run.Id,
                run.Status,
                workerInstanceId,
                claimedRun.LeaseVersion);

            await TryReleaseLeaseOnCancellationAsync(claimedRun);
            throw;
        }
        catch (ResearchRunLeaseLostException exception)
        {
            _logger.LogWarning(
                exception,
                "ResearchRunLeaseLost. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                run.Id,
                run.Status,
                workerInstanceId,
                claimedRun.LeaseVersion);

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "ResearchRunStageFailed. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                run.Id,
                run.Status,
                workerInstanceId,
                claimedRun.LeaseVersion);

            var markedFailed = await _researchRunQueue.MarkFailedAsync(
                claimedRun,
                SafeFailureReason,
                DateTimeOffset.UtcNow,
                cancellationToken);

            if (!markedFailed)
            {
                _logger.LogWarning(
                    "ResearchRunFailureNotPersistedBecauseLeaseWasLost. ResearchRunId: {ResearchRunId}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                    run.Id,
                    workerInstanceId,
                    claimedRun.LeaseVersion);
            }

            return true;
        }
    }

    public Task<bool> ProcessNextQueuedRunAsync(string workerInstanceId, CancellationToken cancellationToken)
    {
        return ProcessNextQueuedRunAsync(workerInstanceId, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1), cancellationToken);
    }

    private async Task ExecuteCurrentStageAsync(
        ClaimedResearchRun claimedRun,
        string workerInstanceId,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        var run = claimedRun.Run;
        var stage = run.Status;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ResearchRunStageStarted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}; LeaseExpiresAt: {LeaseExpiresAt}",
            run.Id,
            stage,
            workerInstanceId,
            claimedRun.LeaseVersion,
            claimedRun.LeaseExpiresAt);

        await RenewLeaseOrThrowAsync(claimedRun, leaseDuration, cancellationToken);
        await ExecuteStageWithHeartbeatAsync(claimedRun, stage, workerInstanceId, leaseDuration, heartbeatInterval, cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "ResearchRunStageCompleted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}; DurationMs: {DurationMs}",
            run.Id,
            stage,
            workerInstanceId,
            claimedRun.LeaseVersion,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task ExecuteStageWithHeartbeatAsync(
        ClaimedResearchRun claimedRun,
        ResearchRunStatus stage,
        string workerInstanceId,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        using var stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stageTask = _stageExecutor.ExecuteAsync(
            new ResearchStageExecutionContext(
                claimedRun.Run.Id,
                claimedRun.Run.ResearchQuestionId,
                stage,
                claimedRun.ResearchQuestion,
                workerInstanceId),
            stageCancellation.Token);
        var heartbeatTask = RunHeartbeatLoopAsync(claimedRun, leaseDuration, heartbeatInterval, stageCancellation.Token);

        var completedTask = await Task.WhenAny(stageTask, heartbeatTask);
        if (completedTask == heartbeatTask)
        {
            stageCancellation.Cancel();
            try
            {
                await stageTask;
            }
            catch (OperationCanceledException)
            {
            }

            await heartbeatTask;
            return;
        }

        stageCancellation.Cancel();
        await IgnoreHeartbeatCancellationAsync(heartbeatTask);
        await stageTask;
    }

    private async Task RunHeartbeatLoopAsync(
        ClaimedResearchRun claimedRun,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(heartbeatInterval, cancellationToken);
            await RenewLeaseOrThrowAsync(claimedRun, leaseDuration, cancellationToken);
        }
    }

    private async Task RenewLeaseOrThrowAsync(
        ClaimedResearchRun claimedRun,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var heartbeatAt = DateTimeOffset.UtcNow;
        var renewed = await _researchRunQueue.RenewLeaseAsync(
            claimedRun,
            heartbeatAt,
            leaseDuration,
            cancellationToken);

        if (!renewed)
        {
            throw new ResearchRunLeaseLostException(claimedRun.Run.Id, claimedRun.WorkerInstanceId, claimedRun.LeaseVersion);
        }

        _logger.LogDebug(
            "ResearchRunLeaseRenewed. ResearchRunId: {ResearchRunId}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}; LeaseExpiresAt: {LeaseExpiresAt}",
            claimedRun.Run.Id,
            claimedRun.WorkerInstanceId,
            claimedRun.LeaseVersion,
            heartbeatAt.Add(leaseDuration));
    }

    private async Task SaveProgressOrThrowAsync(
        ClaimedResearchRun claimedRun,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var saved = await _researchRunQueue.SaveProgressAsync(
            claimedRun,
            DateTimeOffset.UtcNow,
            leaseDuration,
            cancellationToken);

        if (!saved)
        {
            throw new ResearchRunLeaseLostException(claimedRun.Run.Id, claimedRun.WorkerInstanceId, claimedRun.LeaseVersion);
        }
    }

    private static void AdvanceAfterCurrentStage(ResearchRun run)
    {
        switch (run.Status)
        {
            case ResearchRunStatus.Planning:
                run.StartSearching(DateTimeOffset.UtcNow);
                break;
            case ResearchRunStatus.Searching:
                run.StartExtraction(DateTimeOffset.UtcNow);
                break;
            case ResearchRunStatus.Extracting:
                run.StartEvaluation(DateTimeOffset.UtcNow);
                break;
            case ResearchRunStatus.Evaluating:
                run.StartSynthesis(DateTimeOffset.UtcNow);
                break;
            case ResearchRunStatus.Synthesizing:
                run.Complete(DateTimeOffset.UtcNow);
                break;
            default:
                throw new InvalidOperationException($"Cannot advance research run from status {run.Status}.");
        }
    }

    private async Task TryReleaseLeaseOnCancellationAsync(ClaimedResearchRun claimedRun)
    {
        try
        {
            await _researchRunQueue.ReleaseLeaseAsync(claimedRun, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ResearchRunLeaseReleaseFailed. ResearchRunId: {ResearchRunId}; WorkerId: {WorkerId}; LeaseVersion: {LeaseVersion}",
                claimedRun.Run.Id,
                claimedRun.WorkerInstanceId,
                claimedRun.LeaseVersion);
        }
    }

    private static async Task IgnoreHeartbeatCancellationAsync(Task heartbeatTask)
    {
        try
        {
            await heartbeatTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
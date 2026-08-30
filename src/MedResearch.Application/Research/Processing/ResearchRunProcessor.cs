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

    public async Task<bool> ProcessNextQueuedRunAsync(string workerInstanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerInstanceId);

        _logger.LogInformation(
            "ResearchRunClaimAttempt. WorkerInstanceId: {WorkerInstanceId}",
            workerInstanceId);

        var claimedAt = DateTimeOffset.UtcNow;
        var claimedRun = await _researchRunQueue.TryClaimNextQueuedRunAsync(claimedAt, cancellationToken);

        if (claimedRun is null)
        {
            return false;
        }

        var run = claimedRun.Run;

        _logger.LogInformation(
            "ResearchRunClaimed. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            run.Status,
            workerInstanceId);

        try
        {
            await ExecuteCurrentStageAsync(claimedRun, ResearchRunStatus.Planning, workerInstanceId, cancellationToken);
            run.StartSearching(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(claimedRun, cancellationToken);

            await ExecuteCurrentStageAsync(claimedRun, ResearchRunStatus.Searching, workerInstanceId, cancellationToken);
            run.StartExtraction(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(claimedRun, cancellationToken);

            await ExecuteCurrentStageAsync(claimedRun, ResearchRunStatus.Extracting, workerInstanceId, cancellationToken);
            run.StartEvaluation(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(claimedRun, cancellationToken);

            await ExecuteCurrentStageAsync(claimedRun, ResearchRunStatus.Evaluating, workerInstanceId, cancellationToken);
            run.StartSynthesis(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(claimedRun, cancellationToken);

            await ExecuteCurrentStageAsync(claimedRun, ResearchRunStatus.Synthesizing, workerInstanceId, cancellationToken);
            run.Complete(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(claimedRun, cancellationToken);

            _logger.LogInformation(
                "ResearchRunCompleted. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
                run.Id,
                run.Status,
                workerInstanceId);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Research run processing cancelled because host shutdown was requested. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
                run.Id,
                run.Status,
                workerInstanceId);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "ResearchRunFailed. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
                run.Id,
                run.Status,
                workerInstanceId);

            await _researchRunQueue.MarkFailedAsync(
                run.Id,
                SafeFailureReason,
                DateTimeOffset.UtcNow,
                cancellationToken);

            return true;
        }
    }

    private async Task ExecuteCurrentStageAsync(
        ClaimedResearchRun claimedRun,
        ResearchRunStatus stage,
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        var run = claimedRun.Run;

        _logger.LogInformation(
            "ResearchStageStarted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            stage,
            run.Status,
            workerInstanceId);

        await _stageExecutor.ExecuteAsync(
            new ResearchStageExecutionContext(run.Id, stage, claimedRun.ResearchQuestion, workerInstanceId),
            cancellationToken);

        _logger.LogInformation(
            "ResearchStageCompleted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            stage,
            run.Status,
            workerInstanceId);
    }
}

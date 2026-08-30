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
        var run = await _researchRunQueue.TryClaimNextQueuedRunAsync(claimedAt, cancellationToken);

        if (run is null)
        {
            return false;
        }

        _logger.LogInformation(
            "ResearchRunClaimed. ResearchRunId: {ResearchRunId}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            run.Status,
            workerInstanceId);

        try
        {
            await ExecuteCurrentStageAsync(run, ResearchRunStatus.Planning, workerInstanceId, cancellationToken);
            run.StartSearching(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(run, cancellationToken);

            await ExecuteCurrentStageAsync(run, ResearchRunStatus.Searching, workerInstanceId, cancellationToken);
            run.StartExtraction(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(run, cancellationToken);

            await ExecuteCurrentStageAsync(run, ResearchRunStatus.Extracting, workerInstanceId, cancellationToken);
            run.StartEvaluation(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(run, cancellationToken);

            await ExecuteCurrentStageAsync(run, ResearchRunStatus.Evaluating, workerInstanceId, cancellationToken);
            run.StartSynthesis(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(run, cancellationToken);

            await ExecuteCurrentStageAsync(run, ResearchRunStatus.Synthesizing, workerInstanceId, cancellationToken);
            run.Complete(DateTimeOffset.UtcNow);
            await _researchRunQueue.SaveProgressAsync(run, cancellationToken);

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
        ResearchRun run,
        ResearchRunStatus stage,
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ResearchStageStarted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            stage,
            run.Status,
            workerInstanceId);

        await _stageExecutor.ExecuteAsync(stage, cancellationToken);

        _logger.LogInformation(
            "ResearchStageCompleted. ResearchRunId: {ResearchRunId}; Stage: {Stage}; Status: {Status}; WorkerInstanceId: {WorkerInstanceId}",
            run.Id,
            stage,
            run.Status,
            workerInstanceId);
    }
}

using MedResearch.Application.Research.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Research.Processing;

public sealed class BackgroundResearchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundResearchWorker> _logger;
    private readonly ResearchProcessingOptions _options;
    private readonly string _workerInstanceId;

    public BackgroundResearchWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ResearchProcessingOptions> options,
        ILogger<BackgroundResearchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _workerInstanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Background research worker started. WorkerInstanceId: {WorkerInstanceId}",
            _workerInstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>();
                var processedRun = await processor.ProcessNextQueuedRunAsync(_workerInstanceId, stoppingToken);

                if (!processedRun)
                {
                    await DelayUntilNextAttemptAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Background research worker iteration failed. WorkerInstanceId: {WorkerInstanceId}",
                    _workerInstanceId);

                await DelayUntilNextAttemptAsync(stoppingToken);
            }
        }

        _logger.LogInformation(
            "Background research worker stopped. WorkerInstanceId: {WorkerInstanceId}",
            _workerInstanceId);
    }

    private async Task DelayUntilNextAttemptAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_options.IdleDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

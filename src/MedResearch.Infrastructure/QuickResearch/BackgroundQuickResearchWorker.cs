using MedResearch.Application.QuickResearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedResearch.Infrastructure.QuickResearch;

public sealed class BackgroundQuickResearchWorker(IServiceScopeFactory scopes, QuickResearchOptions options,
    ILogger<BackgroundQuickResearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        var workerId = $"quick-{Guid.NewGuid():N}";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<QuickResearchProcessor>()
                    .ProcessNextAsync(workerId, TimeSpan.FromSeconds(options.LeaseDurationSeconds), stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("QuickResearchWorkerIterationFailed. WorkerId: {WorkerId}", workerId);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

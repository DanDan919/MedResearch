namespace MedResearch.Infrastructure.Literature.EuropePmc;

public interface IEuropePmcRetryDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class EuropePmcRetryDelay : IEuropePmcRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
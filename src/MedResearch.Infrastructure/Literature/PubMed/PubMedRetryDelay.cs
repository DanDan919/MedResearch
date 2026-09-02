namespace MedResearch.Infrastructure.Literature.PubMed;

public interface IPubMedRetryDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class PubMedRetryDelay : IPubMedRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
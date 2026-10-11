using System.Threading.RateLimiting;

namespace MedResearch.Infrastructure.Literature.Crossref;

public interface ICrossrefRequestGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}

// Public Crossref has a concurrency quota as well as a request quota. Hold this
// permit through the response body read, not only until headers arrive.
public sealed class CrossrefRequestGate : ICrossrefRequestGate, IDisposable
{
    private readonly SemaphoreSlim _concurrency = new(1, 1);
    private readonly TokenBucketRateLimiter _rate = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true, QueueLimit = 8, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            using var rateLease = await _rate.AcquireAsync(1, cancellationToken);
            if (!rateLease.IsAcquired) throw new ScientificLiteratureRateLimitException("Crossref local limiter rejected the request.");
            return new Permit(_concurrency);
        }
        catch
        {
            _concurrency.Release();
            throw;
        }
    }

    public void Dispose() { _rate.Dispose(); _concurrency.Dispose(); }

    private sealed class Permit(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

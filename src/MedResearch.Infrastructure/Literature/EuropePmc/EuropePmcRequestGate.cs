using System.Threading.RateLimiting;
using MedResearch.Infrastructure.Literature;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.EuropePmc;

public interface IEuropePmcRequestGate
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public sealed class TokenBucketEuropePmcRequestGate : IEuropePmcRequestGate, IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public TokenBucketEuropePmcRequestGate(IOptions<EuropePmcOptions> options)
    {
        var value = options.Value;
        value.Validate();

        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = value.MaxRequestsPerSecond,
            TokensPerPeriod = value.MaxRequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = Math.Max(value.MaxRequestsPerSecond * 4, 4),
            AutoReplenishment = true
        });
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(permitCount: 1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new ScientificLiteratureRateLimitException("Europe PMC local request rate limiter queue rejected the request.");
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}

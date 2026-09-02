using System.Threading.RateLimiting;

namespace MedResearch.Infrastructure.Literature.PubMed;

public interface IPubMedRequestGate
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public sealed class TokenBucketPubMedRequestGate : IPubMedRequestGate, IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public TokenBucketPubMedRequestGate(Microsoft.Extensions.Options.IOptions<PubMedOptions> options)
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
            throw new ScientificLiteratureRateLimitException("PubMed local request rate limiter queue rejected the request.");
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}

public sealed class ScientificLiteratureRateLimitException : Exception
{
    public ScientificLiteratureRateLimitException(string message)
        : base(message)
    {
    }
}
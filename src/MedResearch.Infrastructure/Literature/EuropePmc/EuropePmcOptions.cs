namespace MedResearch.Infrastructure.Literature.EuropePmc;

public sealed class EuropePmcOptions
{
    public const string SectionName = "EuropePmc";
    public const int MaximumResultsPerQuery = 200;
    public const int MaximumPageSize = 100;
    public const int MaximumRetryAttempts = 5;
    public const int MaximumRequestsPerSecond = 5;

    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = "https://www.ebi.ac.uk/europepmc/webservices/rest/";

    public int MaxResultsPerQuery { get; init; } = 10;

    public int PageSize { get; init; } = 25;

    public int TimeoutSeconds { get; init; } = 15;

    public int MaxRequestsPerSecond { get; init; } = 2;

    public int MaxRetryAttempts { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 250;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public int BoundedMaxResultsPerQuery => Math.Clamp(MaxResultsPerQuery, 1, MaximumResultsPerQuery);

    public int BoundedPageSize => Math.Clamp(PageSize, 1, MaximumPageSize);

    public int BoundedMaxRetryAttempts => Math.Clamp(MaxRetryAttempts, 0, MaximumRetryAttempts);

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(Math.Clamp(RetryBaseDelayMilliseconds, 1, 60_000));

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("EuropePmc:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("EuropePmc:TimeoutSeconds must be between 1 and 120.");
        }

        if (MaxResultsPerQuery is < 1 or > MaximumResultsPerQuery)
        {
            throw new InvalidOperationException($"EuropePmc:MaxResultsPerQuery must be between 1 and {MaximumResultsPerQuery}.");
        }

        if (PageSize is < 1 or > MaximumPageSize)
        {
            throw new InvalidOperationException($"EuropePmc:PageSize must be between 1 and {MaximumPageSize}.");
        }

        if (MaxRequestsPerSecond is < 1 or > MaximumRequestsPerSecond)
        {
            throw new InvalidOperationException($"EuropePmc:MaxRequestsPerSecond must be between 1 and {MaximumRequestsPerSecond}.");
        }

        if (MaxRetryAttempts is < 0 or > MaximumRetryAttempts)
        {
            throw new InvalidOperationException($"EuropePmc:MaxRetryAttempts must be between 0 and {MaximumRetryAttempts}.");
        }

        if (RetryBaseDelayMilliseconds is < 1 or > 60_000)
        {
            throw new InvalidOperationException("EuropePmc:RetryBaseDelayMilliseconds must be between 1 and 60000.");
        }
    }
}
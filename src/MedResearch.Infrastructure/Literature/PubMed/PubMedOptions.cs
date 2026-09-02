namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedOptions
{
    public const string SectionName = "PubMed";
    public const int OfficialMaxRequestsPerSecondWithoutApiKey = 3;
    public const int OfficialDefaultMaxRequestsPerSecondWithApiKey = 10;
    public const int MaximumResultsPerQuery = 200;
    public const int MaximumFetchBatchSize = 200;
    public const int MaximumRetryAttempts = 5;

    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/";

    public int MaxResultsPerQuery { get; init; } = 10;

    public int TimeoutSeconds { get; init; } = 15;

    public string Tool { get; init; } = "MedResearch";

    public string? Email { get; init; }

    public string? ApiKey { get; init; }

    public int MaxRequestsPerSecond { get; init; } = 2;

    public int FetchBatchSize { get; init; } = 25;

    public int MaxRetryAttempts { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 250;

    [Obsolete("Use MaxResultsPerQuery. This property is retained only for legacy configuration compatibility.")]
    public int ResultLimit { get; init; }

    [Obsolete("Use MaxRequestsPerSecond. This property is retained only for legacy configuration compatibility.")]
    public int RequestIntervalMilliseconds { get; init; }

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public int BoundedMaxResultsPerQuery => Math.Clamp(MaxResultsPerQuery, 1, MaximumResultsPerQuery);

    public int BoundedFetchBatchSize => Math.Clamp(FetchBatchSize, 1, MaximumFetchBatchSize);

    public int BoundedMaxRetryAttempts => Math.Clamp(MaxRetryAttempts, 0, MaximumRetryAttempts);

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(Math.Clamp(RetryBaseDelayMilliseconds, 1, 60_000));

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public int OfficialMaxRequestsPerSecond => HasApiKey
        ? OfficialDefaultMaxRequestsPerSecondWithApiKey
        : OfficialMaxRequestsPerSecondWithoutApiKey;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("PubMed:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("PubMed:TimeoutSeconds must be between 1 and 120.");
        }

        if (MaxResultsPerQuery is < 1 or > MaximumResultsPerQuery)
        {
            throw new InvalidOperationException($"PubMed:MaxResultsPerQuery must be between 1 and {MaximumResultsPerQuery}.");
        }

        if (FetchBatchSize is < 1 or > MaximumFetchBatchSize)
        {
            throw new InvalidOperationException($"PubMed:FetchBatchSize must be between 1 and {MaximumFetchBatchSize}.");
        }

        if (MaxRetryAttempts is < 0 or > MaximumRetryAttempts)
        {
            throw new InvalidOperationException($"PubMed:MaxRetryAttempts must be between 0 and {MaximumRetryAttempts}.");
        }

        if (RetryBaseDelayMilliseconds is < 1 or > 60_000)
        {
            throw new InvalidOperationException("PubMed:RetryBaseDelayMilliseconds must be between 1 and 60000.");
        }

        if (string.IsNullOrWhiteSpace(Tool) || Tool.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("PubMed:Tool must be a non-empty software name without whitespace.");
        }

        if (!string.IsNullOrWhiteSpace(Email) && (!Email.Contains('@', StringComparison.Ordinal) || Email.Length > 320))
        {
            throw new InvalidOperationException("PubMed:Email must be a valid contact email address when configured.");
        }

        if (MaxRequestsPerSecond < 1)
        {
            throw new InvalidOperationException("PubMed:MaxRequestsPerSecond must be positive.");
        }

        if (MaxRequestsPerSecond > OfficialMaxRequestsPerSecond)
        {
            var apiKeyState = HasApiKey ? "with an API key" : "without an API key";
            throw new InvalidOperationException(
                $"PubMed:MaxRequestsPerSecond cannot exceed {OfficialMaxRequestsPerSecond} {apiKeyState} under the documented NCBI E-utilities policy.");
        }
    }
}
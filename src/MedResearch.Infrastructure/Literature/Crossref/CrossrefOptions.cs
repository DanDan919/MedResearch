using System.Net.Mail;

namespace MedResearch.Infrastructure.Literature.Crossref;

public sealed class CrossrefOptions
{
    public const string SectionName = "Crossref";
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "https://api.crossref.org/";
    public string? Email { get; init; }
    public int MaxResultsPerQuery { get; init; } = 20;
    public int PageSize { get; init; } = 20;
    public int TimeoutSeconds { get; init; } = 20;
    public int MaxResponseBytes { get; init; } = 2_000_000;
    public int BodyReadTimeoutSeconds { get; init; } = 15;
    public int MaxRetryAttempts { get; init; } = 2;
    public int RetryBaseDelayMilliseconds { get; init; } = 500;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !BaseUrl.EndsWith('/') || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Crossref:BaseUrl must be an HTTPS base URI with a trailing slash and no credentials, query or fragment.");
        if (Email is not null && (!MailAddress.TryCreate(Email, out var address) || address.Address != Email || Email.Length > 320))
            throw new InvalidOperationException("Crossref:Email must be a contact email address when configured.");
        if (MaxResultsPerQuery is < 1 or > 200 || PageSize is < 1 or > 100)
            throw new InvalidOperationException("Crossref result limit must be 1..200 and page size 1..100.");
        if (TimeoutSeconds is < 1 or > 120 || BodyReadTimeoutSeconds is < 1 or > 120 || MaxResponseBytes is < 1 or > 10_000_000)
            throw new InvalidOperationException("Crossref timeouts must be 1..120 seconds and byte limit 1..10000000.");
        if (MaxRetryAttempts is < 0 or > 5 || RetryBaseDelayMilliseconds is < 1 or > 60_000)
            throw new InvalidOperationException("Crossref retries must be 0..5 and base delay 1..60000 milliseconds.");
    }
}

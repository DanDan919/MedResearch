namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedOptions
{
    public const string SectionName = "PubMed";

    public string BaseUrl { get; init; } = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/";

    public int ResultLimit { get; init; } = 10;

    public int TimeoutSeconds { get; init; } = 15;

    public string Tool { get; init; } = "MedResearch";

    public string? Email { get; init; }

    public string? ApiKey { get; init; }

    public int RequestIntervalMilliseconds { get; init; } = 350;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));

    public TimeSpan RequestInterval => TimeSpan.FromMilliseconds(Math.Clamp(RequestIntervalMilliseconds, 100, 10_000));

    public int BoundedResultLimit => Math.Clamp(ResultLimit, 1, 50);
}

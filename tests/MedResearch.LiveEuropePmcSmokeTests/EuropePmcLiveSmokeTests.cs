using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature.EuropePmc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.LiveEuropePmcSmokeTests;

public sealed class EuropePmcLiveSmokeTests
{
    [SkippableFact]
    public async Task SearchAsync_WhenExplicitlyEnabled_RetrievesAtLeastOneRealEuropePmcArticle()
    {
        Skip.IfNot(IsTruthy(Environment.GetEnvironmentVariable("MEDRESEARCH_RUN_LIVE_EUROPEPMC_TESTS")),
            "Live Europe PMC smoke tests are opt-in and are not part of normal CI or dotnet test.");

        var options = new EuropePmcOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("EuropePmc__BaseUrl")
                ?? "https://www.ebi.ac.uk/europepmc/webservices/rest/",
            MaxResultsPerQuery = 1,
            PageSize = 1,
            MaxRequestsPerSecond = 1,
            TimeoutSeconds = 15,
            MaxRetryAttempts = 1,
            RetryBaseDelayMilliseconds = 250
        };
        options.Validate();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute),
            Timeout = options.Timeout
        };

        using var gate = new TokenBucketEuropePmcRequestGate(Options.Create(options));
        var source = new EuropePmcScientificLiteratureSource(
            httpClient,
            Options.Create(options),
            gate,
            new EuropePmcRetryDelay(),
            NullLogger<EuropePmcScientificLiteratureSource>.Instance);

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep deprivation"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ScientificLiteratureSourceNames.EuropePmc, result.Source);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
        Assert.Equal(ScientificLiteratureSourceNames.EuropePmc, candidate.Source);
        Assert.True(
            !string.IsNullOrWhiteSpace(candidate.Pmid) ||
            !string.IsNullOrWhiteSpace(candidate.Pmcid) ||
            !string.IsNullOrWhiteSpace(candidate.Doi));
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

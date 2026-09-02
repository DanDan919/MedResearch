using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature.PubMed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.LivePubMedSmokeTests;

public sealed class PubMedLiveSmokeTests
{
    [SkippableFact]
    public async Task SearchAsync_WhenExplicitlyEnabled_RetrievesAtLeastOneRealPubMedArticle()
    {
        Skip.IfNot(IsTruthy(Environment.GetEnvironmentVariable("MEDRESEARCH_RUN_LIVE_PUBMED_TESTS")),
            "Live PubMed smoke tests are opt-in and are not part of normal CI or dotnet test.");

        var email = Environment.GetEnvironmentVariable("PubMed__Email")
            ?? Environment.GetEnvironmentVariable("PUBMED_EMAIL");
        Skip.If(string.IsNullOrWhiteSpace(email), "Set PubMed__Email or PUBMED_EMAIL before running the live PubMed smoke test.");

        var options = new PubMedOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("PubMed__BaseUrl")
                ?? "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/",
            Tool = Environment.GetEnvironmentVariable("PubMed__Tool") ?? "MedResearchLiveSmoke",
            Email = email,
            ApiKey = Environment.GetEnvironmentVariable("PubMed__ApiKey")
                ?? Environment.GetEnvironmentVariable("PUBMED_API_KEY"),
            MaxResultsPerQuery = 1,
            FetchBatchSize = 1,
            MaxRequestsPerSecond = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PubMed__ApiKey") ?? Environment.GetEnvironmentVariable("PUBMED_API_KEY"))
                ? 1
                : 2,
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

        using var gate = new TokenBucketPubMedRequestGate(Options.Create(options));
        var source = new PubMedScientificLiteratureSource(
            httpClient,
            Options.Create(options),
            new PubMedSearchResponseParser(),
            new PubMedArticleMapper(),
            gate,
            new PubMedRetryDelay(),
            NullLogger<PubMedScientificLiteratureSource>.Instance);

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep deprivation[Title/Abstract]"),
            CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("PubMed", result.Source);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Pmid));
        Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
        Assert.Equal("PubMed", candidate.Source);
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.SourceMaterials.EuropePmc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.LiveEuropePmcFullTextSmokeTests;

public sealed class LiveEuropePmcFullTextSmokeTests
{
    [SkippableFact]
    public async Task EuropePmcFullTextEndpoint_ReturnsStructuredSourceMaterialForConfiguredOpenAccessPmcid()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("MEDRESEARCH_RUN_LIVE_EUROPEPMC_FULLTEXT_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        Skip.IfNot(enabled, "Live Europe PMC full-text smoke test is opt-in only.");

        var pmcid = Environment.GetEnvironmentVariable("MEDRESEARCH_LIVE_EUROPEPMC_FULLTEXT_PMCID");
        Assert.False(string.IsNullOrWhiteSpace(pmcid), "Set MEDRESEARCH_LIVE_EUROPEPMC_FULLTEXT_PMCID to a known Europe PMC open-access PMCID before enabling this test.");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.ebi.ac.uk/europepmc/webservices/rest/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        var provider = new EuropePmcFullTextSourceMaterialProvider(
            httpClient,
            Options.Create(new EuropePmcFullTextOptions { MaxRetryAttempts = 1, MaxContentCharacters = 20_000 }),
            new TokenBucketEuropePmcRequestGate(Options.Create(new EuropePmcOptions { MaxRequestsPerSecond = 1 })),
            new EuropePmcRetryDelay(),
            new EuropePmcFullTextXmlParser(),
            NullLogger<EuropePmcFullTextSourceMaterialProvider>.Instance);

        var result = await provider.TryAcquireAsync(
            new SourceMaterialStudyContext(Guid.NewGuid(), Guid.NewGuid(), "Configured live Europe PMC full-text article", null, pmcid, null, null, "EuropePmc"),
            20_000,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("EuropePmc", result.Provider);
        Assert.Equal(pmcid, result.ProviderSourceId);
        Assert.False(string.IsNullOrWhiteSpace(result.Content));
        Assert.NotEmpty(result.SectionNames);
    }
}

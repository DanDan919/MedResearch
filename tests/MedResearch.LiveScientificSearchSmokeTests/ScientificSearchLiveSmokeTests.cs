using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.DependencyInjection;
using MedResearch.Infrastructure.Literature.Crossref;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.Literature.PubMed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MedResearch.LiveScientificSearchSmokeTests;

// Explicit external contract verification only. Outside MedResearch.slnx/CI;
// no database writes, research admission, source-to-LLM export or paid API calls.
public sealed class ScientificSearchLiveSmokeTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData(ScientificLiteratureSourceNames.PubMed)]
    [InlineData(ScientificLiteratureSourceNames.EuropePmc)]
    [InlineData(ScientificLiteratureSourceNames.Crossref)]
    public async Task SmallOfficialSearch_UsesProductionAdapterAndParsesRealMetadata(string sourceName)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("MEDRESEARCH_RUN_LIVE_SEARCH_TESTS") == "true",
            "Explicit live search opt-in required; never part of normal CI.");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MedResearch"] = "Host=127.0.0.1;Database=unused_live_search_no_database_calls;Username=unused",
            ["ResearchProcessing:Enabled"] = "false",
            ["EuropePmcFullText:Enabled"] = "false",
            ["AI:Provider"] = "OpenAI"
        };
        foreach (var source in new[] { "PubMed", "EuropePmc", "Crossref" })
        {
            settings[$"{source}:Enabled"] = (source == sourceName).ToString();
            settings[$"{source}:MaxResultsPerQuery"] = "3";
            settings[$"{source}:PageSize"] = "3";
            settings[$"{source}:MaxRetryAttempts"] = "0";
            settings[$"{source}:MaxRequestsPerSecond"] = "1";
        }
        settings["PubMed:Email"] = Environment.GetEnvironmentVariable("PubMed__Email");
        settings["Crossref:Email"] = Environment.GetEnvironmentVariable("Crossref__Email");
        settings["PubMed:FetchBatchSize"] = "3";
        var services = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        var counter = new Counter();
        services.AddHttpClient<PubMedScientificLiteratureSource>().AddHttpMessageHandler(() => new CountingHandler(counter));
        services.AddHttpClient<EuropePmcScientificLiteratureSource>().AddHttpMessageHandler(() => new CountingHandler(counter));
        services.AddHttpClient<CrossrefScientificLiteratureSource>().AddHttpMessageHandler(() => new CountingHandler(counter));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var adapter = Assert.Single(scope.ServiceProvider.GetServices<IScientificLiteratureSource>());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await adapter.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(),
            "vaping[Title/Abstract] AND health[Title/Abstract]"), deadline.Token);
        Assert.Equal(sourceName, result.Source);
        Assert.InRange(result.Candidates.Count, 1, 3);
        Assert.All(result.Candidates, candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
            Assert.True(candidate.Pmid is not null || candidate.Pmcid is not null || candidate.Doi is not null);
            Assert.Equal(sourceName, candidate.Source);
        });
        Assert.Equal(sourceName == "PubMed" ? 2 : 1, counter.Requests);
        output.WriteLine($"Source={sourceName}; requests={counter.Requests}; normalized articles={result.Candidates.Count}; no DB/LLM calls.");
    }

    private sealed class Counter { public int Requests; }
    private sealed class CountingHandler(Counter counter) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref counter.Requests);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

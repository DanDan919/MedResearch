using System.Net;
using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.Literature.PubMed;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests;

public sealed class QuickScientificSourcesTests
{
    [Fact]
    public async Task ActualAdapters_UseBoundedFakeHttp_AndPreserveMultiSourceDiscovery()
    {
        using var pubHandler = new FixtureHandler("pubmed.invalid");
        using var europeHandler = new FixtureHandler("europe.invalid");
        var gateway = Gateway(pubHandler, europeHandler);
        var runId = Guid.NewGuid();
        var plan = QuickQueryPreparation.Prepare("Magnesium insomnia adults");
        var all = new List<QuickCandidate>();
        foreach (var provider in gateway.EnabledProviders)
        {
            var page = await gateway.SearchAsync(provider, runId, Guid.NewGuid(), gateway.PrepareQuery(provider, plan.Queries[0]), CancellationToken.None);
            Assert.Equal(8, page.Candidates.Length);
            all.AddRange(page.Candidates);
        }
        Assert.Equal(2, pubHandler.Requests.Count);
        Assert.Single(europeHandler.Requests);
        Assert.Contains("retmax=8", pubHandler.Requests[0].Query);
        Assert.Equal(8, Parameters(pubHandler.Requests[1])["id"].Split(',').Length);
        Assert.Contains("pageSize=8", europeHandler.Requests[0].Query);
        var selected = new QuickPublicationSelector().Select(runId, plan, all.ToArray()).Sources;
        Assert.Equal(5, selected.Length);
        Assert.All(selected, s => { Assert.Equal(2, s.Discoveries.Length); Assert.Null(s.Abstract); Assert.Equal(runId, s.QuickRunId); });
    }

    [Fact]
    public async Task ZeroResults_SkipsEfetch()
    {
        using var pub = new FixtureHandler("pubmed.invalid") { Zero = true };
        using var europe = new FixtureHandler("europe.invalid") { Zero = true };
        var gateway = Gateway(pub, europe);
        foreach (var provider in gateway.EnabledProviders)
        {
            var page = await gateway.SearchAsync(provider, Guid.NewGuid(), Guid.NewGuid(), "sleep", CancellationToken.None);
            Assert.Equal(0, page.ReturnedCount);
            Assert.Empty(page.Candidates);
        }
        Assert.Single(pub.Requests);
        Assert.Single(europe.Requests);
    }

    [Fact]
    public async Task CallerCancellation_ReachesAdapterWithoutHttp()
    {
        using var pub = new FixtureHandler("pubmed.invalid");
        using var europe = new FixtureHandler("europe.invalid");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(pub, europe)
            .SearchAsync("PubMed", Guid.NewGuid(), Guid.NewGuid(), "sleep", cancellation.Token));
        Assert.Empty(pub.Requests);
    }

    [Fact]
    public async Task OptionalRequestCap_DoesNotChangeDefaultDeepLimit()
    {
        using var pub = new FixtureHandler("pubmed.invalid");
        var source = PubMed(pub);
        await source.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid(), "sleep"), CancellationToken.None);
        Assert.Contains("retmax=20", pub.Requests[0].Query);
        Assert.Equal(20, new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep").BoundResults(20));
        Assert.Equal(3, new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep", 8).BoundResults(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void InvalidRequestCap_IsRejected(int maximum) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep", maximum).BoundResults(20));

    [Theory]
    [InlineData("https://doi.org/10.1234/ABC", "10.1234/abc")]
    [InlineData("doi:10.1234/ABC", "10.1234/abc")]
    public async Task SharedIdentifierNormalization_IsReused(string incoming, string expected)
    {
        var gateway = new QuickScientificSources([new StaticSource("PubMed", Candidate() with { Doi = incoming, Pmcid = " pmc123 " })]);
        var page = await gateway.SearchAsync("PubMed", Guid.NewGuid(), Guid.NewGuid(), "sleep", CancellationToken.None);
        Assert.Equal(expected, Assert.Single(page.Candidates).Doi);
        Assert.Equal("PMC123", page.Candidates[0].Pmcid);
    }

    [Theory]
    [InlineData("no-id")]
    [InlineData("garbage-doi")]
    [InlineData("zero-pmid")]
    [InlineData("oversize-abstract")]
    public async Task UnusableMetadata_IsCountedAsRejected_NotInvented(string kind)
    {
        var candidate = kind switch
        {
            "no-id" => Candidate() with { Pmid = null },
            "garbage-doi" => Candidate() with { Doi = "not-a-doi" },
            "zero-pmid" => Candidate() with { Pmid = "0" },
            _ => Candidate() with { Abstract = new string('x', 16001) }
        };
        var page = await new QuickScientificSources([new StaticSource("PubMed", candidate)])
            .SearchAsync("PubMed", Guid.NewGuid(), Guid.NewGuid(), "sleep", CancellationToken.None);
        Assert.Empty(page.Candidates);
        Assert.Equal(1, page.RejectedCount);
    }

    [Fact]
    public void Crossref_IsNotIncludedInQuickCoverage() => Assert.Empty(new QuickScientificSources([new StaticSource("Crossref", Candidate())]).EnabledProviders);

    private static ScientificStudyCandidate Candidate() => new("1", null, null, "Sleep publication", null, null, null, null, null, null, [], [], "1", "PubMed");
    private static QuickScientificSources Gateway(FixtureHandler pub, FixtureHandler europe) => new([PubMed(pub),
        new EuropePmcScientificLiteratureSource(new HttpClient(europe) { BaseAddress = new Uri("https://europe.invalid/") },
            Options.Create(new EuropePmcOptions { MaxRetryAttempts = 0 }), new ImmediateGate(), new ImmediateDelay(), NullLogger<EuropePmcScientificLiteratureSource>.Instance)]);
    private static PubMedScientificLiteratureSource PubMed(FixtureHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://pubmed.invalid/") },
        Options.Create(new PubMedOptions { MaxRetryAttempts = 0 }), new PubMedSearchResponseParser(), new PubMedArticleMapper(),
        new ImmediateGate(), new ImmediateDelay(), NullLogger<PubMedScientificLiteratureSource>.Instance);
    private static Dictionary<string, string> Parameters(Uri uri) => uri.Query.TrimStart('?').Split('&')
        .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
    private sealed class ImmediateGate : IPubMedRequestGate, IEuropePmcRequestGate
    { public ValueTask WaitAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; } }
    private sealed class ImmediateDelay : IPubMedRetryDelay, IEuropePmcRetryDelay
    { public Task DelayAsync(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; } }
    private sealed class StaticSource(string provider, ScientificStudyCandidate candidate) : IScientificLiteratureSource
    {
        public string SourceName => provider;
        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken token) =>
            Task.FromResult(new ScientificSearchResult(provider, DateTimeOffset.UtcNow, 1, [candidate]));
    }
    private sealed class FixtureHandler(string host) : HttpMessageHandler
    {
        public bool Zero { get; init; }
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Assert.Equal(host, uri.Host);
            Requests.Add(uri);
            var parameters = Parameters(uri);
            string body;
            if (uri.AbsolutePath.EndsWith("esearch.fcgi", StringComparison.Ordinal))
            {
                var count = Zero ? 0 : int.Parse(parameters["retmax"]);
                body = JsonSerializer.Serialize(new { esearchresult = new { count = count.ToString(), idlist = Enumerable.Range(1, count).Select(i => i.ToString()).ToArray() } });
            }
            else if (uri.AbsolutePath.EndsWith("efetch.fcgi", StringComparison.Ordinal))
                body = "<PubmedArticleSet>" + string.Join("", parameters["id"].Split(',').Select(id =>
                    $"<PubmedArticle><MedlineCitation><PMID>{id}</PMID><Article><ArticleTitle>Magnesium for insomnia in adults.</ArticleTitle></Article></MedlineCitation><PubmedData><ArticleIdList><ArticleId IdType=\"doi\">10.1234/p{id}</ArticleId></ArticleIdList></PubmedData></PubmedArticle>")) + "</PubmedArticleSet>";
            else
            {
                Assert.EndsWith("search", uri.AbsolutePath);
                var count = Zero ? 0 : int.Parse(parameters["pageSize"]);
                body = JsonSerializer.Serialize(new { hitCount = count, nextCursorMark = "*", resultList = new { result = Enumerable.Range(1, count)
                    .Select(i => new { id = i.ToString(), source = "MED", pmid = i.ToString(), pmcid = $"PMC{i}", doi = $"10.1234/p{i}", title = "Magnesium for insomnia in adults." }).ToArray() } });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}

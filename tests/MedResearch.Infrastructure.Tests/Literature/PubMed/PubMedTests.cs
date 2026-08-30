using System.Net;
using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature.PubMed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Literature.PubMed;

public sealed class PubMedParsingTests
{
    [Fact]
    public void ParsePmids_ReturnsDistinctPmids()
    {
        var parser = new PubMedSearchResponseParser();
        var json = ReadFixture("pubmed-esearch-two-results.json");

        var pmids = parser.ParsePmids(json);

        Assert.Equal(["12345678", "87654321"], pmids);
    }

    [Fact]
    public void ParsePmids_RejectsInvalidJson()
    {
        var parser = new PubMedSearchResponseParser();

        Assert.Throws<PubMedResponseException>(() => parser.ParsePmids("not-json"));
    }

    [Fact]
    public void MapArticles_CapturesAvailableMetadataWithoutInventingMissingValues()
    {
        var mapper = new PubMedArticleMapper();
        var xml = ReadFixture("pubmed-efetch-two-articles.xml");

        var candidates = mapper.MapArticles(xml).ToArray();

        Assert.Equal(2, candidates.Length);

        var complete = candidates[0];
        Assert.Equal("12345678", complete.Pmid);
        Assert.Equal("10.1000/sleep.2024.001", complete.Doi);
        Assert.Equal("Sleep deprivation and working memory in adults.", complete.Title);
        Assert.Equal("Journal of Sleep Research", complete.Journal);
        Assert.Equal(new DateOnly(2024, 2, 3), complete.PublicationDate);
        Assert.Equal(2024, complete.PublicationYear);
        Assert.Equal(2, complete.PublicationMonth);
        Assert.Equal(3, complete.PublicationDay);
        Assert.Contains("BACKGROUND: Sleep loss may affect cognition.", complete.Abstract);
        Assert.Contains("RESULTS: Working memory performance declined after sleep deprivation.", complete.Abstract);
        Assert.Equal(["Journal Article", "Clinical Trial"], complete.PublicationTypes);
        Assert.Equal(["Ada Lovelace", "Sleep Research Group"], complete.Authors);
        Assert.Equal("PubMed", complete.Source);

        var incomplete = candidates[1];
        Assert.Equal("87654321", incomplete.Pmid);
        Assert.Null(incomplete.Doi);
        Assert.Null(incomplete.Abstract);
        Assert.Equal("Neuro Notes", incomplete.Journal);
        Assert.Null(incomplete.PublicationDate);
        Assert.Equal(2023, incomplete.PublicationYear);
        Assert.Null(incomplete.PublicationMonth);
        Assert.Null(incomplete.PublicationDay);
        Assert.Equal(["Review"], incomplete.PublicationTypes);
        Assert.Equal(["Curie"], incomplete.Authors);
    }

    [Fact]
    public void MapArticles_RejectsMalformedXml()
    {
        var mapper = new PubMedArticleMapper();

        Assert.Throws<PubMedResponseException>(() => mapper.MapArticles(ReadFixture("pubmed-invalid.xml")));
    }

    private static string ReadFixture(string fileName)
    {
        return File.ReadAllText(Path.Combine("Literature", "PubMed", "Fixtures", fileName));
    }
}

public sealed class PubMedScientificLiteratureSourceTests
{
    [Fact]
    public async Task SearchAsync_UsesESearchThenEFetchAndMapsResults()
    {
        var handler = new StubPubMedHandler(
            ReadFixture("pubmed-esearch-two-results.json"),
            ReadFixture("pubmed-efetch-two-articles.xml"));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var source = CreateSource(client, new PubMedOptions
        {
            ResultLimit = 2,
            Tool = "MedResearchTests",
            Email = "tests@example.test",
            ApiKey = "test-key",
            RequestIntervalMilliseconds = 100
        });

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep deprivation"),
            CancellationToken.None);

        Assert.Equal("PubMed", result.Source);
        Assert.Equal(2, result.ReturnedResultCount);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.AbsolutePath.EndsWith("/esearch.fcgi", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.AbsolutePath.EndsWith("/efetch.fcgi", StringComparison.Ordinal));
        Assert.Contains("tool=MedResearchTests", handler.Requests[0].Query);
        Assert.Contains("email=tests%40example.test", handler.Requests[0].Query);
        Assert.Contains("api_key=test-key", handler.Requests[0].Query);
        Assert.Contains("retmax=2", handler.Requests[0].Query);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyResultWhenESearchFindsNoPmids()
    {
        var handler = new StubPubMedHandler("""
            { "esearchresult": { "idlist": [] } }
            """, ReadFixture("pubmed-efetch-two-articles.xml"));
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/")
        };
        var source = CreateSource(client, new PubMedOptions { RequestIntervalMilliseconds = 100 });

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "no results"),
            CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.ReturnedResultCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_ConvertsRateLimitResponseToSourceException()
    {
        var source = CreateSource(
            new HttpClient(new StatusCodeHandler(HttpStatusCode.TooManyRequests))
            {
                BaseAddress = new Uri("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/")
            },
            new PubMedOptions { RequestIntervalMilliseconds = 100 });

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "rate limited"), CancellationToken.None));

        Assert.Equal("PubMed rate limit was reached.", exception.Message);
    }

    private static PubMedScientificLiteratureSource CreateSource(HttpClient client, PubMedOptions options)
    {
        return new PubMedScientificLiteratureSource(
            client,
            Options.Create(options),
            new PubMedSearchResponseParser(),
            new PubMedArticleMapper(),
            NullLogger<PubMedScientificLiteratureSource>.Instance);
    }

    private static string ReadFixture(string fileName)
    {
        return File.ReadAllText(Path.Combine("Literature", "PubMed", "Fixtures", fileName));
    }

    private sealed class StubPubMedHandler : HttpMessageHandler
    {
        private readonly string _searchResponse;
        private readonly string _fetchResponse;

        public StubPubMedHandler(string searchResponse, string fetchResponse)
        {
            _searchResponse = searchResponse;
            _fetchResponse = fetchResponse;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            Requests.Add(request.RequestUri);

            var content = request.RequestUri.AbsolutePath.EndsWith("/esearch.fcgi", StringComparison.Ordinal)
                ? _searchResponse
                : _fetchResponse;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class StatusCodeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StatusCodeHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}

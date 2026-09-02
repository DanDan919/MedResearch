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

        var result = parser.Parse(json);

        Assert.Equal(2, result.TotalAvailableCount);
        Assert.Equal(["12345678", "87654321"], result.Pmids);
    }

    [Fact]
    public void ParsePmids_RejectsInvalidJson()
    {
        var parser = new PubMedSearchResponseParser();

        Assert.Throws<PubMedResponseException>(() => parser.Parse("not-json"));
    }

    [Fact]
    public void ParsePmids_RejectsMalformedPmidValues()
    {
        var parser = new PubMedSearchResponseParser();

        var exception = Assert.Throws<PubMedResponseException>(() => parser.Parse("""
            { "esearchresult": { "idlist": ["123", "PMID-456"] } }
            """));

        Assert.Contains("invalid PMID", exception.Message);
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
    public void MapArticles_HandlesRealisticXmlEdgeCases()
    {
        var mapper = new PubMedArticleMapper();
        var candidates = mapper.MapArticles(ReadFixture("pubmed-efetch-edge-cases.xml")).ToArray();

        Assert.Equal(3, candidates.Length);

        var structured = candidates[0];
        Assert.Equal("11111111", structured.Pmid);
        Assert.Equal("10.1234/abc.x", structured.Doi);
        Assert.Equal("Unicode β cognition & encoded entities.", structured.Title);
        Assert.Contains("BACKGROUND: β cognition improves.", structured.Abstract);
        Assert.Contains("METHODS: Participants completed nested recall tasks.", structured.Abstract);
        Assert.Equal(["Jane Doe", "Consortium Group"], structured.Authors);

        var electronic = candidates[1];
        Assert.Equal("22222222", electronic.Pmid);
        Assert.Equal(new DateOnly(2025, 5, 9), electronic.PublicationDate);
        Assert.Equal(2025, electronic.PublicationYear);
        Assert.Equal(5, electronic.PublicationMonth);
        Assert.Equal(9, electronic.PublicationDay);

        var medline = candidates[2];
        Assert.Equal("33333333", medline.Pmid);
        Assert.Null(medline.PublicationDate);
        Assert.Equal(2024, medline.PublicationYear);
        Assert.Equal(1, medline.PublicationMonth);
        Assert.Null(medline.PublicationDay);
    }

    [Fact]
    public void MapArticles_SkipsMalformedRecordsWithoutStablePubMedIdentity()
    {
        var mapper = new PubMedArticleMapper();
        var candidates = mapper.MapArticles("""
            <PubmedArticleSet>
              <PubmedArticle><MedlineCitation><PMID>abc</PMID><Article><ArticleTitle>Bad PMID.</ArticleTitle></Article></MedlineCitation></PubmedArticle>
              <PubmedArticle><MedlineCitation><Article><ArticleTitle>No PMID.</ArticleTitle></Article></MedlineCitation></PubmedArticle>
              <PubmedArticle><MedlineCitation><PMID>44444444</PMID><Article><ArticleTitle>Good PMID.</ArticleTitle></Article></MedlineCitation></PubmedArticle>
            </PubmedArticleSet>
            """);

        var candidate = Assert.Single(candidates);
        Assert.Equal("44444444", candidate.Pmid);
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
    public async Task SearchAsync_UsesOfficialParametersAndDoesNotIncludeApiKeyWhenAbsent()
    {
        var query = "sleep deprivation AND (working memory[Title/Abstract] OR \"executive function\") -animals β";
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.OK, BuildSearchJson("12345678", "87654321")),
            Response(HttpStatusCode.OK, ReadFixture("pubmed-efetch-two-articles.xml")));
        var gate = new RecordingRequestGate();
        var source = CreateSource(handler, new PubMedOptions
        {
            Tool = "MedResearchTests",
            Email = "tests@example.test",
            MaxRequestsPerSecond = 2,
            MaxResultsPerQuery = 2,
            FetchBatchSize = 25,
            MaxRetryAttempts = 0
        }, gate: gate);

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), query),
            CancellationToken.None);

        Assert.Equal("PubMed", result.Source);
        Assert.Equal(2, result.ReturnedResultCount);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(2, gate.WaitCount);

        var searchParameters = QueryParameters(handler.Requests[0]);
        Assert.EndsWith("/esearch.fcgi", handler.Requests[0].AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("pubmed", searchParameters["db"]);
        Assert.Equal(query, searchParameters["term"]);
        Assert.Equal("2", searchParameters["retmax"]);
        Assert.Equal("json", searchParameters["retmode"]);
        Assert.Equal("relevance", searchParameters["sort"]);
        Assert.Equal("MedResearchTests", searchParameters["tool"]);
        Assert.Equal("tests@example.test", searchParameters["email"]);
        Assert.False(searchParameters.ContainsKey("api_key"));

        var fetchParameters = QueryParameters(handler.Requests[1]);
        Assert.EndsWith("/efetch.fcgi", handler.Requests[1].AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("pubmed", fetchParameters["db"]);
        Assert.Equal("12345678,87654321", fetchParameters["id"]);
        Assert.Equal("xml", fetchParameters["retmode"]);
        Assert.Equal("MedResearchTests", fetchParameters["tool"]);
        Assert.Equal("tests@example.test", fetchParameters["email"]);
        Assert.False(fetchParameters.ContainsKey("api_key"));
    }

    [Fact]
    public async Task SearchAsync_IncludesApiKeyWhenConfigured()
    {
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.OK, BuildSearchJson("12345678")),
            Response(HttpStatusCode.OK, BuildFetchXml("12345678")));
        var source = CreateSource(handler, new PubMedOptions
        {
            Tool = "MedResearchTests",
            Email = "tests@example.test",
            ApiKey = "test-api-key",
            MaxRequestsPerSecond = 10,
            MaxResultsPerQuery = 1,
            FetchBatchSize = 1,
            MaxRetryAttempts = 0
        });

        await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep"), CancellationToken.None);

        Assert.All(handler.Requests, request => Assert.Equal("test-api-key", QueryParameters(request)["api_key"]));
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyResultWhenESearchFindsNoPmidsAndDoesNotCallEFetch()
    {
        var handler = new RecordingPubMedHandler(Response(HttpStatusCode.OK, """
            { "esearchresult": { "count": "0", "idlist": [] } }
            """));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 0 });

        var result = await source.SearchAsync(
            new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "no results"),
            CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.ReturnedResultCount);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/esearch.fcgi", handler.Requests[0].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_BatchesEFetchRequestsAndMapsAllRecords()
    {
        var handler = new RecordingPubMedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/esearch.fcgi", StringComparison.Ordinal))
            {
                return Response(HttpStatusCode.OK, BuildSearchJson("1", "2", "3", "4", "5", "6", "7"))(request);
            }

            var ids = QueryParameters(request.RequestUri!)["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
            return Response(HttpStatusCode.OK, BuildFetchXml(ids))(request);
        });
        var source = CreateSource(handler, new PubMedOptions
        {
            ApiKey = "test-api-key",
            MaxRequestsPerSecond = 10,
            MaxResultsPerQuery = 7,
            FetchBatchSize = 3,
            MaxRetryAttempts = 0
        });

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "batching"), CancellationToken.None);

        var fetchIds = handler.Requests
            .Where(request => request.AbsolutePath.EndsWith("/efetch.fcgi", StringComparison.Ordinal))
            .Select(request => QueryParameters(request)["id"])
            .ToArray();

        Assert.Equal(7, result.Candidates.Count);
        Assert.Equal(["1,2,3", "4,5,6", "7"], fetchIds);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesESearchPmidsBeforeEFetch()
    {
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.OK, """
                { "esearchresult": { "count": "3", "idlist": ["123", "123", "456"] } }
                """),
            Response(HttpStatusCode.OK, BuildFetchXml("123", "456")));
        var source = CreateSource(handler, new PubMedOptions { FetchBatchSize = 10, MaxRetryAttempts = 0 });

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "duplicates"), CancellationToken.None);

        Assert.Equal(2, result.ReturnedResultCount);
        Assert.Equal("123,456", QueryParameters(handler.Requests[1])["id"]);
        Assert.Equal(["123", "456"], result.Candidates.Select(candidate => candidate.Pmid!).ToArray());
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesDuplicateEFetchArticles()
    {
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.OK, BuildSearchJson("123")),
            Response(HttpStatusCode.OK, BuildFetchXml("123", "123")));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 0 });

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "duplicate fetch"), CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("123", candidate.Pmid);
    }

    [Fact]
    public async Task SearchAsync_Retries429AndRespectsRetryAfter()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.TooManyRequests, "rate", retryAfter: TimeSpan.FromSeconds(2)),
            Response(HttpStatusCode.OK, BuildSearchJson("123")),
            Response(HttpStatusCode.OK, BuildFetchXml("123")));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "retry"), CancellationToken.None);

        Assert.Single(result.Candidates);
        var delay = Assert.Single(retryDelay.Delays);
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchAsync_RetriesTransient5xxWithBoundedAttempts()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.InternalServerError, "server one"),
            Response(HttpStatusCode.BadGateway, "server two"),
            Response(HttpStatusCode.OK, BuildSearchJson("123")),
            Response(HttpStatusCode.OK, BuildFetchXml("123")));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 2, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "server retry"), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Equal(2, retryDelay.Delays.Count);
    }

    [Fact]
    public async Task SearchAsync_DoesNotRetryPermanent400()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingPubMedHandler(Response(HttpStatusCode.BadRequest, "bad query"));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 3 }, retryDelay: retryDelay);

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "bad"), CancellationToken.None));

        Assert.Equal("PubMed request failed.", exception.Message);
        Assert.Empty(retryDelay.Delays);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_DoesNotExposeApiKeyInHttpExceptionMessage()
    {
        var handler = new RecordingPubMedHandler(Response(HttpStatusCode.BadRequest, "bad query"));
        var source = CreateSource(handler, new PubMedOptions
        {
            ApiKey = "super-secret-test-key",
            MaxRequestsPerSecond = 10,
            MaxRetryAttempts = 0
        });

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "bad"), CancellationToken.None));

        Assert.DoesNotContain("super-secret-test-key", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_DoesNotRetryMalformedSuccessPayload()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingPubMedHandler(Response(HttpStatusCode.OK, "not-json"));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 3 }, retryDelay: retryDelay);

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "malformed"), CancellationToken.None));

        Assert.Equal("PubMed returned an invalid response.", exception.Message);
        Assert.Empty(retryDelay.Delays);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_RetriesNetworkTransportFailure()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingPubMedHandler(
            Throw(new HttpRequestException("network unavailable")),
            Response(HttpStatusCode.OK, BuildSearchJson("123")),
            Response(HttpStatusCode.OK, BuildFetchXml("123")));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "network retry"), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Single(retryDelay.Delays);
    }

    [Fact]
    public async Task SearchAsync_CancellationDuringBackoffAbortsRetry()
    {
        using var cts = new CancellationTokenSource();
        var retryDelay = new RecordingRetryDelay(cts);
        var handler = new RecordingPubMedHandler(
            Response(HttpStatusCode.TooManyRequests, "rate"),
            Response(HttpStatusCode.OK, BuildSearchJson("123")));
        var source = CreateSource(handler, new PubMedOptions { MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "cancel"), cts.Token));

        Assert.Single(retryDelay.Delays);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_WhenDisabledFailsClearlyWithoutHttpRequest()
    {
        var handler = new RecordingPubMedHandler(Response(HttpStatusCode.OK, BuildSearchJson("123")));
        var source = CreateSource(handler, new PubMedOptions { Enabled = false });

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "disabled"), CancellationToken.None));

        Assert.Equal("PubMed source is disabled by configuration.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    private static PubMedScientificLiteratureSource CreateSource(
        RecordingPubMedHandler handler,
        PubMedOptions? options = null,
        RecordingRequestGate? gate = null,
        RecordingRetryDelay? retryDelay = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new PubMedScientificLiteratureSource(
            client,
            Options.Create(options ?? new PubMedOptions()),
            new PubMedSearchResponseParser(),
            new PubMedArticleMapper(),
            gate ?? new RecordingRequestGate(),
            retryDelay ?? new RecordingRetryDelay(),
            NullLogger<PubMedScientificLiteratureSource>.Instance);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Response(HttpStatusCode statusCode, string body, TimeSpan? retryAfter = null)
    {
        return request =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };

            if (retryAfter.HasValue)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            }

            return response;
        };
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Throw(Exception exception)
    {
        return _ => throw exception;
    }

    private static string BuildSearchJson(params string[] pmids)
    {
        return $$"""
            { "esearchresult": { "count": "{{pmids.Length}}", "idlist": [{{string.Join(", ", pmids.Select(pmid => $"\"{pmid}\""))}}] } }
            """;
    }

    private static string BuildFetchXml(params string[] pmids)
    {
        var articles = string.Join(Environment.NewLine, pmids.Select(pmid => $$"""
              <PubmedArticle>
                <MedlineCitation>
                  <PMID>{{pmid}}</PMID>
                  <Article>
                    <Journal><JournalIssue><PubDate><Year>2026</Year></PubDate></JournalIssue><Title>Test Journal</Title></Journal>
                    <ArticleTitle>Article {{pmid}}</ArticleTitle>
                    <Abstract><AbstractText>Result for {{pmid}}.</AbstractText></Abstract>
                  </Article>
                </MedlineCitation>
              </PubmedArticle>
            """));

        return $$"""
            <PubmedArticleSet>
            {{articles}}
            </PubmedArticleSet>
            """;
    }

    private static IReadOnlyDictionary<string, string> QueryParameters(Uri uri)
    {
        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadFixture(string fileName)
    {
        return File.ReadAllText(Path.Combine("Literature", "PubMed", "Fixtures", fileName));
    }

    private sealed class RecordingPubMedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = [];
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _dynamicResponse;

        public RecordingPubMedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public RecordingPubMedHandler(Func<HttpRequestMessage, HttpResponseMessage> dynamicResponse)
        {
            _dynamicResponse = dynamicResponse;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotNull(request.RequestUri);
            Requests.Add(request.RequestUri);

            var response = _dynamicResponse is not null
                ? _dynamicResponse(request)
                : _responses.Dequeue()(request);

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingRequestGate : IPubMedRequestGate
    {
        public int WaitCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRetryDelay : IPubMedRetryDelay
    {
        private readonly CancellationTokenSource? _cancellationTokenSource;

        public RecordingRetryDelay(CancellationTokenSource? cancellationTokenSource = null)
        {
            _cancellationTokenSource = cancellationTokenSource;
        }

        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            if (_cancellationTokenSource is not null)
            {
                _cancellationTokenSource.Cancel();
                return Task.FromCanceled(cancellationToken);
            }

            return Task.CompletedTask;
        }
    }
}
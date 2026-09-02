using System.Net;
using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.Literature.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Literature.EuropePmc;

public sealed class ScientificIdentifierNormalizerTests
{
    [Theory]
    [InlineData(" 123456 ", "123456")]
    [InlineData("PMID-123", null)]
    [InlineData("", null)]
    public void NormalizePmid_UsesNumericSemantics(string? value, string? expected)
    {
        Assert.Equal(expected, ScientificIdentifierNormalizer.NormalizePmid(value));
    }

    [Theory]
    [InlineData("PMC123456", "PMC123456")]
    [InlineData("pmc123456", "PMC123456")]
    [InlineData("123456", "PMC123456")]
    [InlineData("PMCABC", null)]
    public void NormalizePmcid_NormalizesPrefixAndRejectsGarbage(string value, string? expected)
    {
        Assert.Equal(expected, ScientificIdentifierNormalizer.NormalizePmcid(value));
    }

    [Theory]
    [InlineData("10.1234/ABC.X", "10.1234/abc.x")]
    [InlineData("doi:10.1234/ABC.X", "10.1234/abc.x")]
    [InlineData("https://doi.org/10.1234/ABC.X", "10.1234/abc.x")]
    [InlineData(" http://dx.doi.org/10.1234/ABC.X ", "10.1234/abc.x")]
    public void NormalizeDoi_StripsKnownPrefixesAndLowercasesForComparison(string value, string expected)
    {
        Assert.Equal(expected, ScientificIdentifierNormalizer.NormalizeDoi(value));
    }
}

public sealed class EuropePmcScientificLiteratureSourceTests
{
    [Fact]
    public async Task SearchAsync_UsesOfficialSearchParametersAndMapsCoreMetadata()
    {
        var query = "sleep deprivation AND (working memory OR cognition) β";
        var handler = new RecordingEuropePmcHandler(Response(HttpStatusCode.OK, PageJson("next-1", Article("12345678", "PMC123456", "https://doi.org/10.1000/ABC.X"))));
        var gate = new RecordingRequestGate();
        var source = CreateSource(handler, new EuropePmcOptions
        {
            MaxResultsPerQuery = 1,
            PageSize = 1,
            MaxRetryAttempts = 0
        }, gate: gate);

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), query), CancellationToken.None);

        Assert.Equal("EuropePmc", result.Source);
        Assert.Equal(1, result.ReturnedResultCount);
        Assert.Equal(1, gate.WaitCount);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("12345678", candidate.Pmid);
        Assert.Equal("PMC123456", candidate.Pmcid);
        Assert.Equal("10.1000/abc.x", candidate.Doi);
        Assert.Equal("Europe PMC article title β & cognition", candidate.Title);
        Assert.Equal("Europe PMC abstract text.", candidate.Abstract);
        Assert.Equal("Journal of Europe PMC", candidate.Journal);
        Assert.Equal(new DateOnly(2026, 2, 3), candidate.PublicationDate);
        Assert.Equal(2026, candidate.PublicationYear);
        Assert.Equal(2, candidate.PublicationMonth);
        Assert.Equal(3, candidate.PublicationDay);
        Assert.Equal(["journal article", "research article"], candidate.PublicationTypes);
        Assert.Equal(["Ada Lovelace", "Grace Hopper"], candidate.Authors);
        Assert.Equal("MED:12345678", candidate.ProviderRecordId);

        var parameters = QueryParameters(handler.Requests[0]);
        Assert.EndsWith("/search", handler.Requests[0].AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(query, parameters["query"]);
        Assert.Equal("json", parameters["format"]);
        Assert.Equal("core", parameters["resultType"]);
        Assert.Equal("1", parameters["pageSize"]);
        Assert.Equal("*", parameters["cursorMark"]);
    }

    [Fact]
    public async Task SearchAsync_UsesCursorPaginationUntilConfiguredLimit()
    {
        var handler = new RecordingEuropePmcHandler(request =>
        {
            var cursor = QueryParameters(request.RequestUri!)["cursorMark"];
            return cursor == "*"
                ? Response(HttpStatusCode.OK, PageJson("cursor-2", Article("1", "PMC1", "10.1000/one"), Article("2", "PMC2", "10.1000/two")))(request)
                : Response(HttpStatusCode.OK, PageJson("cursor-3", Article("3", "PMC3", "10.1000/three")))(request);
        });
        var source = CreateSource(handler, new EuropePmcOptions { MaxResultsPerQuery = 3, PageSize = 2, MaxRetryAttempts = 0 });

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "sleep"), CancellationToken.None);

        Assert.Equal(["1", "2", "3"], result.Candidates.Select(candidate => candidate.Pmid!).ToArray());
        Assert.Equal(["*", "cursor-2"], handler.Requests.Select(uri => QueryParameters(uri)["cursorMark"]).ToArray());
        Assert.Equal(["2", "1"], handler.Requests.Select(uri => QueryParameters(uri)["pageSize"]).ToArray());
    }

    [Fact]
    public async Task SearchAsync_ZeroResultsAreSuccessfulAndDoNotRequestMorePages()
    {
        var handler = new RecordingEuropePmcHandler(Response(HttpStatusCode.OK, """
            { "hitCount": 0, "nextCursorMark": "*", "resultList": { "result": [] } }
            """));
        var source = CreateSource(handler, new EuropePmcOptions { MaxRetryAttempts = 0 });

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "rare"), CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.ReturnedResultCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_Retries429AndRespectsRetryAfter()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingEuropePmcHandler(
            Response(HttpStatusCode.TooManyRequests, "rate", retryAfter: TimeSpan.FromSeconds(2)),
            Response(HttpStatusCode.OK, PageJson("next", Article("123", "PMC123", "10.1000/retry"))));
        var source = CreateSource(handler, new EuropePmcOptions { MaxResultsPerQuery = 1, MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        var result = await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "retry"), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(retryDelay.Delays));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchAsync_DoesNotRetryPermanent400OrMalformedJson()
    {
        var retryDelay = new RecordingRetryDelay();
        var badRequestHandler = new RecordingEuropePmcHandler(Response(HttpStatusCode.BadRequest, "bad query"));
        var badRequestSource = CreateSource(badRequestHandler, new EuropePmcOptions { MaxRetryAttempts = 3 }, retryDelay: retryDelay);

        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            badRequestSource.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "bad"), CancellationToken.None));
        Assert.Empty(retryDelay.Delays);
        Assert.Single(badRequestHandler.Requests);

        retryDelay = new RecordingRetryDelay();
        var malformedHandler = new RecordingEuropePmcHandler(Response(HttpStatusCode.OK, "not-json"));
        var malformedSource = CreateSource(malformedHandler, new EuropePmcOptions { MaxRetryAttempts = 3 }, retryDelay: retryDelay);

        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            malformedSource.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "bad json"), CancellationToken.None));
        Assert.Equal("Europe PMC returned an invalid response.", exception.Message);
        Assert.Empty(retryDelay.Delays);
        Assert.Single(malformedHandler.Requests);
    }

    [Fact]
    public async Task SearchAsync_CancellationDuringBackoffAbortsRetry()
    {
        using var cts = new CancellationTokenSource();
        var retryDelay = new RecordingRetryDelay(cts);
        var handler = new RecordingEuropePmcHandler(
            Response(HttpStatusCode.InternalServerError, "server"),
            Response(HttpStatusCode.OK, PageJson("next", Article("123", "PMC123", "10.1000/cancel"))));
        var source = CreateSource(handler, new EuropePmcOptions { MaxResultsPerQuery = 1, MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "cancel"), cts.Token));

        Assert.Single(retryDelay.Delays);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_SkipsRecordsWithoutStableIdentifierOrTitleAndDeduplicatesResults()
    {
        var handler = new RecordingEuropePmcHandler(Response(HttpStatusCode.OK, $$"""
            {
              "hitCount": 4,
              "nextCursorMark": "*",
              "resultList": {
                "result": [
                  {{Article("123", "PMC123", "10.1000/dup")}},
                  {{Article("123", "PMC123", "10.1000/dup")}},
                  { "id": "no-title", "source": "MED", "pmid": "456", "doi": "10.1000/no-title" },
                  { "id": "no-id", "source": "MED", "title": "No identifier" }
                ]
              }
            }
            """));
        var source = CreateSource(handler, new EuropePmcOptions { MaxRetryAttempts = 0 });

        var candidate = Assert.Single((await source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), "dedupe"), CancellationToken.None)).Candidates);
        Assert.Equal("123", candidate.Pmid);
    }

    private static EuropePmcScientificLiteratureSource CreateSource(
        RecordingEuropePmcHandler handler,
        EuropePmcOptions? options = null,
        RecordingRequestGate? gate = null,
        RecordingRetryDelay? retryDelay = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.ebi.ac.uk/europepmc/webservices/rest/"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new EuropePmcScientificLiteratureSource(
            client,
            Options.Create(options ?? new EuropePmcOptions()),
            gate ?? new RecordingRequestGate(),
            retryDelay ?? new RecordingRetryDelay(),
            NullLogger<EuropePmcScientificLiteratureSource>.Instance);
    }

    private static string PageJson(string nextCursorMark, params string[] articles)
    {
        return $$"""
            {
              "hitCount": {{articles.Length}},
              "nextCursorMark": "{{nextCursorMark}}",
              "resultList": { "result": [ {{string.Join(",", articles)}} ] }
            }
            """;
    }

    private static string Article(string pmid, string pmcid, string doi)
    {
        return $$"""
            {
              "id": "{{pmid}}",
              "source": "MED",
              "pmid": "{{pmid}}",
              "pmcid": "{{pmcid}}",
              "doi": "{{doi}}",
              "title": "Europe PMC article title β & cognition",
              "abstractText": "Europe PMC abstract text.",
              "journalTitle": "Journal of Europe PMC",
              "firstPublicationDate": "2026-02-03",
              "authorList": { "author": [ { "fullName": "Ada Lovelace" }, { "fullName": "Grace Hopper" } ] },
              "pubTypeList": { "pubType": [ "journal article", "research article" ] }
            }
            """;
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Response(HttpStatusCode statusCode, string body, TimeSpan? retryAfter = null)
    {
        return _ =>
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

    private sealed class RecordingEuropePmcHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = [];
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _dynamicResponse;

        public RecordingEuropePmcHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public RecordingEuropePmcHandler(Func<HttpRequestMessage, HttpResponseMessage> dynamicResponse)
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

    private sealed class RecordingRequestGate : IEuropePmcRequestGate
    {
        public int WaitCount { get; private set; }

        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRetryDelay : IEuropePmcRetryDelay
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

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.Crossref;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Literature.Crossref;

public sealed class CrossrefTests
{
    [Theory]
    [InlineData("(vaping[Title/Abstract] OR vape*[tiab]) AND \"health effects\"[tiab]", "vaping vape health effects")]
    [InlineData("sleep deprivation AND working memory", "sleep deprivation working memory")]
    [InlineData("\"total sleep deprivation\" AND (\"working memory\" OR \"working-memory\") AND healthy AND adults", "total sleep deprivation working memory working-memory healthy adults")]
    [InlineData("β cognition", "β cognition")]
    public void PrepareQuery_ExplicitLexicalExpansionIsIdempotent(string query, string expected)
    {
        var source = Create(new Handler());
        Assert.Equal(expected, source.PrepareQuery(query));
        Assert.Equal(expected, source.PrepareQuery(source.PrepareQuery(query)));
    }

    [Theory]
    [InlineData("sleep NOT animals")]
    [InlineData("sleep[MeSH Terms]")]
    [InlineData("sleep[unknown]")]
    [InlineData("TITLE_ABS:sleep")]
    [InlineData("sleep -animals")]
    [InlineData("(sleep AND memory")]
    public async Task PrepareQuery_UnsupportedConstraintsFailBeforeHttp(string query)
    {
        var handler = new Handler();
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler), query));
        Assert.Equal(LiteratureProviderFailureCategory.ProviderProtocolError, exception.FailureCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Search_UsesEncodedParametersAndMapsOnlyReportedMetadata()
    {
        var handler = new Handler(Ok(Page(null, new
        {
            DOI = "https://doi.org/10.1234/ABC.X", type = "journal-article",
            title = new[] { "Vaping & β health" }, @abstract = "<jats:p>Source-reported results.</jats:p>",
            author = new object[] { new { given = "Ada", family = "Lovelace" }, new { name = "Study Consortium" }, new { family = "Curie" } },
            published = new Dictionary<string, object> { ["date-parts"] = new[] { new[] { 2025 } } },
            indexed = new Dictionary<string, object> { ["date-parts"] = new[] { new[] { 2026, 10, 11 } } }
        })));
        var gate = new Gate();
        var result = await Search(Create(handler, new CrossrefOptions { Email = "tests@example.test", MaxResultsPerQuery = 3 }, gate));
        var request = Assert.Single(handler.Requests);
        var parameters = Query(request);
        Assert.Equal("/works", request.AbsolutePath);
        Assert.Equal("vaping health effects", parameters["query"]);
        Assert.Equal("type:journal-article,has-abstract:true", parameters["filter"]);
        Assert.Equal("*", parameters["cursor"]);
        Assert.Equal("score", parameters.GetValueOrDefault("sort"));
        Assert.Equal("desc", parameters.GetValueOrDefault("order"));
        Assert.Equal("3", parameters["rows"]);
        Assert.Equal("tests@example.test", parameters["mailto"]);
        Assert.False(parameters.ContainsKey("api_key"));
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Crossref", result.Source);
        Assert.Equal("10.1234/abc.x", candidate.Doi);
        Assert.Equal(candidate.Doi, candidate.ProviderRecordId);
        Assert.Equal("Vaping & β health", candidate.Title);
        Assert.Equal("<jats:p>Source-reported results.</jats:p>", candidate.Abstract);
        Assert.Equal(["Ada Lovelace", "Study Consortium", "Curie"], candidate.Authors);
        Assert.Equal(["journal-article"], candidate.PublicationTypes);
        Assert.Equal(2025, candidate.PublicationYear);
        Assert.Null(candidate.PublicationDate);
        Assert.Null(candidate.PublicationMonth);
        Assert.Null(candidate.Journal);
        Assert.Null(candidate.Pmid);
        Assert.Null(candidate.Pmcid);
        Assert.Equal(1, gate.Acquired);
        Assert.Equal(1, gate.Released);
    }

    [Fact]
    public async Task Search_PaginatesThreeTwoWithEncodedCursorAndDeduplicatesDoi()
    {
        var handler = new Handler(
            Ok(Page("next+/=", Article("10.1234/a"), Article("10.1234/b"), Article("10.1234/c"))),
            Ok(Page("unused", Article("10.1234/C"), Article("10.1234/d"))));
        var result = await Search(Create(handler, new CrossrefOptions { MaxResultsPerQuery = 5, PageSize = 3 }));
        Assert.Equal(4, result.Candidates.Count);
        Assert.Equal(["3", "2"], handler.Requests.Select(uri => Query(uri)["rows"]));
        Assert.Equal("next+/=", Query(handler.Requests[1])["cursor"]);
        Assert.All(handler.Requests, uri => Assert.False(Query(uri).ContainsKey("mailto")));
        Assert.All(handler.Requests, uri =>
        {
            Assert.Equal("vaping health effects", Query(uri)["query"]);
            Assert.Equal("type:journal-article,has-abstract:true", Query(uri)["filter"]);
            Assert.Equal("score", Query(uri).GetValueOrDefault("sort"));
            Assert.Equal("desc", Query(uri).GetValueOrDefault("order"));
        });
    }

    [Fact]
    public async Task Search_ZeroResultsIsSuccessfulAndDoesNotFetchDetails()
    {
        var handler = new Handler(Ok(Page("unused")));
        var result = await Search(Create(handler));
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.ReturnedResultCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Search_NoStableIdentityIsSkippedAndRawRecordBudgetStopsPagination()
    {
        var handler = new Handler(Ok(Page("next", new { title = new[] { "No DOI" }, type = "journal-article" })));
        var result = await Search(Create(handler, new CrossrefOptions { MaxResultsPerQuery = 1 }));
        Assert.Empty(result.Candidates);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("10.xyz/article")]
    [InlineData("10.1234/")]
    [InlineData("10.1234/has whitespace")]
    public async Task Search_GarbageDoiCannotBecomeCanonicalStudyIdentity(string doi)
    {
        var result = await Search(Create(new Handler(Ok(Page(null, Article(doi))))));
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Search_CursorCycleIsBounded()
    {
        var handler = new Handler(Ok(Page("next", Article("10.1234/a"))), Ok(Page("*", Article("10.1234/b"))));
        var result = await Search(Create(handler, new CrossrefOptions { MaxResultsPerQuery = 10, PageSize = 1 }));
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Search_RetriesOnlyTransientStatusAndRespectsRetryAfter(int status)
    {
        var rejected = new HttpResponseMessage((HttpStatusCode)status);
        rejected.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
        var delay = new Delay();
        var gate = new Gate();
        var handler = new Handler(rejected, Ok(Page(null, Article("10.1234/a"))));
        var result = await Search(Create(handler, gate: gate, delay: delay));
        Assert.Single(result.Candidates);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(delay.Delays));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, gate.Released);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Search_DoesNotRetryPermanentStatus(int status)
    {
        var handler = new Handler(new HttpResponseMessage((HttpStatusCode)status));
        var delay = new Delay();
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler, delay: delay)));
        Assert.Equal(LiteratureProviderFailureCategory.ProviderProtocolError, exception.FailureCategory);
        Assert.Single(handler.Requests);
        Assert.Empty(delay.Delays);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"error\"}")]
    public async Task Search_InvalidSuccessDoesNotRetryOrBecomeZero(string body)
    {
        var handler = new Handler(Ok(body));
        var delay = new Delay();
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler, delay: delay)));
        Assert.Equal(LiteratureProviderFailureCategory.InvalidResponse, exception.FailureCategory);
        Assert.Single(handler.Requests);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task Search_ByteCapFailsWithoutRetryAndReleasesPermit()
    {
        var gate = new Gate();
        var handler = new Handler(Ok(new string('x', 101)));
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler,
            new CrossrefOptions { MaxResponseBytes = 100 }, gate)));
        Assert.Equal(LiteratureProviderFailureCategory.ResponseTooLarge, exception.FailureCategory);
        Assert.Equal(1, gate.Released);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Search_NetworkErrorsAreBoundedAndTransportSecretsAreNotReflected()
    {
        var handler = new Handler(exception: new HttpRequestException("https://host/?credential=do-not-reflect"));
        var delay = new Delay();
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler, delay: delay)));
        Assert.Equal(LiteratureProviderFailureCategory.NetworkFailure, exception.FailureCategory);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delay.Delays.Count);
        Assert.DoesNotContain("do-not-reflect", exception.ToString());
    }

    [Fact]
    public async Task Search_TimeoutRetriesAreBounded()
    {
        var handler = new Handler(exception: new TaskCanceledException());
        var exception = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => Search(Create(handler)));
        Assert.Equal(LiteratureProviderFailureCategory.Timeout, exception.FailureCategory);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Search_CancellationDuringBackoffStopsRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var delay = new Delay { Cancel = cancellation };
        var handler = new Handler(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Search(Create(handler, delay: delay), cancellationToken: cancellation.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Gate_CancelledConcurrentCallerCannotStealHeldPermit()
    {
        using var gate = new CrossrefRequestGate();
        await using var permit = await gate.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.AcquireAsync(cancellation.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void Options_RejectInvalidBoundsAndCredentialBearingUri()
    {
        Assert.Throws<InvalidOperationException>(() => new CrossrefOptions { MaxResultsPerQuery = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new CrossrefOptions { PageSize = 101 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new CrossrefOptions { MaxRetryAttempts = 6 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new CrossrefOptions { BaseUrl = "https://user:password@example.test/" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new CrossrefOptions { Email = "not-an-email" }.Validate());
    }

    private static CrossrefScientificLiteratureSource Create(Handler handler, CrossrefOptions? options = null, Gate? gate = null, Delay? delay = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.crossref.org/") }, Options.Create(options ?? new CrossrefOptions()),
        gate ?? new Gate(), delay ?? new Delay(), NullLogger<CrossrefScientificLiteratureSource>.Instance);

    private static Task<ScientificSearchResult> Search(CrossrefScientificLiteratureSource source, string query = "vaping health effects", CancellationToken cancellationToken = default) =>
        source.SearchAsync(new ScientificSearchRequest(Guid.NewGuid(), Guid.NewGuid(), query), cancellationToken);
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static object Article(string doi) => new { DOI = doi, title = new[] { "Source-reported article" }, type = "journal-article" };
    private static string Page(string? cursor, params object[] items) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["status"] = "ok", ["message-type"] = "work-list",
        ["message"] = new Dictionary<string, object?> { ["total-results"] = items.Length, ["items"] = items, ["next-cursor"] = cursor }
    });
    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private readonly Exception? _exception;
        public List<Uri> Requests { get; } = [];
        public Handler(params HttpResponseMessage[] responses) { _responses = new(responses); }
        public Handler(Exception exception) { _responses = new(); _exception = exception; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return _exception is null ? Task.FromResult(_responses.Dequeue()) : Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class Gate : ICrossrefRequestGate
    {
        public int Acquired { get; private set; }
        public int Released { get; private set; }
        public ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Acquired++;
            return ValueTask.FromResult<IAsyncDisposable>(new Permit(this));
        }
        private sealed class Permit(Gate gate) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { gate.Released++; return ValueTask.CompletedTask; }
        }
    }

    private sealed class Delay : ICrossrefRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public CancellationTokenSource? Cancel { get; init; }
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            Cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}

using System.Net;
using System.Text;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.Literature.PubMed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.Literature;

public sealed class ProviderRuntimeIntegrityTests
{
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task BodyCapIsInclusiveWithoutContentLength(int bytes, bool accepted)
    {
        using var content = new StreamContent(new NonSeekableStream(new byte[bytes]));
        if (accepted)
            Assert.Equal(bytes, (await BoundedProviderBody.ReadAsync(content, 7, TimeSpan.FromSeconds(15), CancellationToken.None)).Length);
        else
            await Assert.ThrowsAsync<ProviderResponseTooLargeException>(() => BoundedProviderBody.ReadAsync(content, 7, TimeSpan.FromSeconds(15), CancellationToken.None));
    }

    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    [InlineData("PubMedFetch")]
    public async Task OversizedSuccessIsTypedFailureAndNotRetried(string operation)
    {
        var handler = new FakeHandler(operation == "PubMedFetch" ? [SearchBody, new string('x', 129)] : [new string('x', 129)]);
        var source = CreateSource(operation, handler, 128, retries: 2);
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => source.SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(LiteratureProviderFailureCategory.ResponseTooLarge, failure.FailureCategory);
        Assert.Equal(operation == "PubMedFetch" ? 2 : 1, handler.Calls);
    }

    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    [InlineData("PubMedFetch")]
    public async Task StalledSuccessBodyTimesOutAfterHeadersUsingDeterministicClock(string operation)
    {
        var clock = new DeadlineClock();
        var handler = new FakeHandler(operation == "PubMedFetch" ? [SearchBody] : [], () => new StallStream(clock.Fire));
        var source = CreateSource(operation, handler, 1000, clock);
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => source.SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(LiteratureProviderFailureCategory.Timeout, failure.FailureCategory);
        Assert.Equal(operation == "PubMedFetch" ? 2 : 1, handler.Calls);
    }

    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    public async Task HostCancellationWhileReadingRemainsCancellation(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHandler([], () => new StallStream(cancellation.Cancel));
        var source = CreateSource(operation, handler, 1000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SearchAsync(Request(), cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("EuropePmc", "{}")]
    [InlineData("EuropePmc", "{\"hitCount\":0}")]
    [InlineData("PubMedSearch", "{}")]
    [InlineData("PubMedSearch", "[]")]
    [InlineData("PubMedFetch", "<invalid>")]
    public async Task MalformedSuccessIsNotZeroOrRetried(string operation, string body)
    {
        var handler = new FakeHandler(operation == "PubMedFetch" ? [SearchBody, body] : [body]);
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => CreateSource(operation, handler, 1000, retries: 2).SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(LiteratureProviderFailureCategory.InvalidResponse, failure.FailureCategory);
        Assert.Equal(operation == "PubMedFetch" ? 2 : 1, handler.Calls);
    }

    [Theory]
    [InlineData("EuropePmc", 429, LiteratureProviderFailureCategory.RateLimited)]
    [InlineData("PubMedSearch", 429, LiteratureProviderFailureCategory.RateLimited)]
    [InlineData("EuropePmc", 503, LiteratureProviderFailureCategory.ProviderProtocolError)]
    [InlineData("PubMedSearch", 500, LiteratureProviderFailureCategory.ProviderProtocolError)]
    [InlineData("EuropePmc", 404, LiteratureProviderFailureCategory.ProviderProtocolError)]
    [InlineData("PubMedSearch", 400, LiteratureProviderFailureCategory.ProviderProtocolError)]
    public async Task NonSuccessOutcomeIsTypedAndErrorBodyIsNeverRead(string operation, int status, LiteratureProviderFailureCategory category)
    {
        var handler = new FakeHandler([], () => new StallStream(() => throw new InvalidOperationException("Error body must not be read")), (HttpStatusCode)status);
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => CreateSource(operation, handler, 1000).SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(category, failure.FailureCategory);
        Assert.DoesNotContain("api_key", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancelledLimiterStopsBeforeHttp()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHandler([]);
        var gate = new Gate(cancellation);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var source = new EuropePmcScientificLiteratureSource(client, Options.Create(new EuropePmcOptions()), gate, gate, NullLogger<EuropePmcScientificLiteratureSource>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SearchAsync(Request(), cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private const string SearchBody = "{\"esearchresult\":{\"count\":\"1\",\"idlist\":[\"123\"]}}";
    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    public async Task BodyTimeoutRetriesRemainBounded(string operation)
    {
        var clock = new DeadlineClock();
        var handler = new FakeHandler([], () => new StallStream(clock.Fire));
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => CreateSource(operation, handler, 1000, clock, retries: 2).SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(LiteratureProviderFailureCategory.Timeout, failure.FailureCategory);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    public async Task ValidSuccessBodyExactlyAtCapIsAccepted(string operation)
    {
        var body = operation == "EuropePmc" ? "{\"hitCount\":0,\"resultList\":{\"result\":[]}}" : "{\"esearchresult\":{\"idlist\":[]}}";
        var result = await CreateSource(operation, new FakeHandler([body]), Encoding.UTF8.GetByteCount(body)).SearchAsync(Request(), CancellationToken.None);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.ReturnedResultCount);
    }

    [Theory]
    [InlineData("EuropePmc")]
    [InlineData("PubMedSearch")]
    public async Task NetworkExceptionDoesNotExposeSecretRequestUri(string operation)
    {
        const string fakeSecret = "synthetic-sensitive-value";
        var failure = await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => CreateSource(operation, new ThrowingHandler(fakeSecret), 1000).SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(LiteratureProviderFailureCategory.NetworkFailure, failure.FailureCategory);
        Assert.False(failure.ToString().Contains(fakeSecret, StringComparison.Ordinal));
    }
    private sealed class ThrowingHandler(string fakeSecret) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new HttpRequestException($"transport failed at api_key={fakeSecret}");
    }
    private static ScientificSearchRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), "query");
    private static IScientificLiteratureSource CreateSource(string operation, HttpMessageHandler handler, int cap, TimeProvider? clock = null, int retries = 0)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var gate = new Gate();
        if (operation == "EuropePmc") return new EuropePmcScientificLiteratureSource(client,
            Options.Create(new EuropePmcOptions { MaxResponseBytes = cap, MaxRetryAttempts = retries }), gate, gate, NullLogger<EuropePmcScientificLiteratureSource>.Instance, clock);
        return new PubMedScientificLiteratureSource(client,
            Options.Create(new PubMedOptions { MaxSearchResponseBytes = cap, MaxFetchResponseBytes = cap, MaxRetryAttempts = retries }),
            new PubMedSearchResponseParser(), new PubMedArticleMapper(), gate, gate, NullLogger<PubMedScientificLiteratureSource>.Instance, clock);
    }

    private sealed class Gate(CancellationTokenSource? cancel = null) : IEuropePmcRequestGate, IPubMedRequestGate, IEuropePmcRetryDelay, IPubMedRetryDelay
    {
        public ValueTask WaitAsync(CancellationToken token) { cancel?.Cancel(); token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public Task DelayAsync(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class FakeHandler(string[] bodies, Func<Stream>? stream = null, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var index = Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = index < bodies.Length ? new StringContent(bodies[index]) : new StreamContent(stream!()) });
        }
    }
    private class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class StallStream(Action started) : NonSeekableStream([])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            started();
            return new ValueTask<int>(Task.Delay(Timeout.Infinite, token).ContinueWith<int>(task => { task.GetAwaiter().GetResult(); return 0; }, TaskScheduler.Default));
        }
    }
    private sealed class DeadlineClock : TimeProvider
    {
        private Action? _fire;
        public void Fire() => _fire!();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _fire = () => callback(state);
            return new TestTimer();
        }
        private sealed class TestTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

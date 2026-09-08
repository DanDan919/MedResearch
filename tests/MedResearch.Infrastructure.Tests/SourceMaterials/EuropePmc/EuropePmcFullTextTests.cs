using System.Net;
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.EuropePmc;
using MedResearch.Infrastructure.SourceMaterials.EuropePmc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Tests.SourceMaterials.EuropePmc;

public sealed class EuropePmcFullTextXmlParserTests
{
    [Fact]
    public void Parse_ExtractsNamespacedJatsSectionsAndLicense()
    {
        var parser = new EuropePmcFullTextXmlParser();

        var parsed = parser.Parse("""
            <article xmlns="http://jats.nlm.nih.gov">
              <front>
                <article-meta>
                  <title-group><article-title>Sleep &amp; recall</article-title></title-group>
                  <abstract><p>Abstract sentence one.</p><p>Abstract sentence two.</p></abstract>
                  <permissions>
                    <license xlink:href="https://creativecommons.org/licenses/by/4.0/" xmlns:xlink="http://www.w3.org/1999/xlink">
                      <license-p>Creative Commons Attribution License</license-p>
                    </license>
                  </permissions>
                </article-meta>
              </front>
              <body>
                <sec><title>Methods</title><p>Randomized methods text.</p></sec>
                <sec><title>Results</title><p>Recall improved in 120 adults.</p></sec>
              </body>
            </article>
            """, 10_000);

        Assert.False(parsed.WasTruncated);
        Assert.Equal(["Title", "Abstract", "Methods", "Results"], parsed.SectionNames);
        Assert.Contains("## Methods", parsed.Content, StringComparison.Ordinal);
        Assert.Contains("Recall improved in 120 adults.", parsed.Content, StringComparison.Ordinal);
        Assert.Equal("Creative Commons Attribution License", parsed.License);
        Assert.Equal("https://creativecommons.org/licenses/by/4.0/", parsed.LicenseUrl);
    }

    [Fact]
    public void Parse_TruncatesContentAtConfiguredBoundary()
    {
        var parser = new EuropePmcFullTextXmlParser();

        var parsed = parser.Parse("""
            <article>
              <body><sec><title>Body</title><p>abcdefghijklmnopqrstuvwxyz</p></sec></body>
            </article>
            """, 20);

        Assert.True(parsed.WasTruncated);
        Assert.True(parsed.Content.Length <= 20);
    }

    [Fact]
    public void Parse_RejectsDtdAndEmptySourceText()
    {
        var parser = new EuropePmcFullTextXmlParser();

        Assert.Throws<EuropePmcFullTextResponseException>(() => parser.Parse("""
            <!DOCTYPE article [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <article><body><sec><title>Body</title><p>&xxe;</p></sec></body></article>
            """, 1000));

        Assert.Throws<EuropePmcFullTextResponseException>(() => parser.Parse("<article />", 1000));
    }
}

public sealed class EuropePmcFullTextSourceMaterialProviderTests
{
    [Fact]
    public async Task TryAcquireAsync_SkipsStudiesWithoutPmcidWithoutHttpRequest()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, FullTextXml()));
        var gate = new RecordingRequestGate();
        var provider = CreateProvider(handler, gate: gate);

        var result = await provider.TryAcquireAsync(CreateStudy(pmcid: null), 10_000, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, gate.WaitCount);
    }

    [Fact]
    public async Task TryAcquireAsync_MapsStructuredFullTextCandidate()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, FullTextXml()));
        var gate = new RecordingRequestGate();
        var provider = CreateProvider(handler, gate: gate);

        var result = await provider.TryAcquireAsync(CreateStudy(), 10_000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(SourceMaterialType.StructuredFullText, result.Type);
        Assert.Equal("EuropePmc", result.Provider);
        Assert.Equal("PMC123456", result.ProviderSourceId);
        Assert.Equal("EuropePmcFullTextXml", result.RetrievalMethod);
        Assert.Equal(SourceMaterialAccessStatus.OpenAccess, result.AccessStatus);
        Assert.Contains("Randomized methods text.", result.Content, StringComparison.Ordinal);
        Assert.Equal(["Title", "Abstract", "Methods"], result.SectionNames);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/PMC123456/fullTextXML", handler.Requests[0].AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(1, gate.WaitCount);
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsUnavailableFor404WithoutRetry()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingHandler(Response(HttpStatusCode.NotFound, "not found"));
        var provider = CreateProvider(handler, retryDelay: retryDelay);

        var result = await provider.TryAcquireAsync(CreateStudy(), 10_000, CancellationToken.None);

        Assert.Null(result);
        Assert.Single(handler.Requests);
        Assert.Empty(retryDelay.Delays);
    }

    [Fact]
    public async Task TryAcquireAsync_Retries429ThenParsesSuccess()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingHandler(
            Response(HttpStatusCode.TooManyRequests, "rate limited", TimeSpan.FromMilliseconds(5)),
            Response(HttpStatusCode.OK, FullTextXml()));
        var provider = CreateProvider(handler, options: new EuropePmcFullTextOptions { MaxRetryAttempts = 1, RetryBaseDelayMilliseconds = 1 }, retryDelay: retryDelay);

        var result = await provider.TryAcquireAsync(CreateStudy(), 10_000, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(5), Assert.Single(retryDelay.Delays));
    }

    [Fact]
    public async Task TryAcquireAsync_DoesNotRetryMalformedSuccessfulXml()
    {
        var retryDelay = new RecordingRetryDelay();
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, "<article>"));
        var provider = CreateProvider(handler, options: new EuropePmcFullTextOptions { MaxRetryAttempts = 3 }, retryDelay: retryDelay);

        await Assert.ThrowsAsync<EuropePmcFullTextResponseException>(() => provider.TryAcquireAsync(CreateStudy(), 10_000, CancellationToken.None));
        Assert.Empty(retryDelay.Delays);
        Assert.Single(handler.Requests);
    }

    private static EuropePmcFullTextSourceMaterialProvider CreateProvider(
        RecordingHandler handler,
        EuropePmcFullTextOptions? options = null,
        RecordingRequestGate? gate = null,
        RecordingRetryDelay? retryDelay = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.ebi.ac.uk/europepmc/webservices/rest/"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new EuropePmcFullTextSourceMaterialProvider(
            client,
            Options.Create(options ?? new EuropePmcFullTextOptions()),
            gate ?? new RecordingRequestGate(),
            retryDelay ?? new RecordingRetryDelay(),
            new EuropePmcFullTextXmlParser(),
            NullLogger<EuropePmcFullTextSourceMaterialProvider>.Instance);
    }

    private static SourceMaterialStudyContext CreateStudy(string? pmcid = "PMC123456")
    {
        return new SourceMaterialStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Sleep and recall",
            "12345678",
            pmcid,
            "10.1000/example",
            "Abstract text.",
            "EuropePmc");
    }

    private static string FullTextXml()
    {
        return """
            <article xmlns="http://jats.nlm.nih.gov">
              <front>
                <article-meta>
                  <title-group><article-title>Sleep and recall</article-title></title-group>
                  <abstract><p>Abstract text.</p></abstract>
                </article-meta>
              </front>
              <body><sec><title>Methods</title><p>Randomized methods text.</p></sec></body>
            </article>
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = [];

        public RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotNull(request.RequestUri);
            Requests.Add(request.RequestUri);
            return Task.FromResult(_responses.Dequeue()(request));
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
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }
}

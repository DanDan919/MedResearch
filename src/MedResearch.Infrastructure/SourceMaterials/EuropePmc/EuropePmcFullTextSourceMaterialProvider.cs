using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.EuropePmc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.SourceMaterials.EuropePmc;

public sealed class EuropePmcFullTextSourceMaterialProvider : ISourceMaterialProvider
{
    private const int MaximumErrorBodyBytes = 1024;
    private const int MaximumXmlBytes = 2_000_000;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly EuropePmcFullTextOptions _options;
    private readonly IEuropePmcRequestGate _requestGate;
    private readonly IEuropePmcRetryDelay _retryDelay;
    private readonly EuropePmcFullTextXmlParser _parser;
    private readonly ILogger<EuropePmcFullTextSourceMaterialProvider> _logger;

    public EuropePmcFullTextSourceMaterialProvider(
        HttpClient httpClient,
        IOptions<EuropePmcFullTextOptions> options,
        IEuropePmcRequestGate requestGate,
        IEuropePmcRetryDelay retryDelay,
        EuropePmcFullTextXmlParser parser,
        ILogger<EuropePmcFullTextSourceMaterialProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _requestGate = requestGate;
        _retryDelay = retryDelay;
        _parser = parser;
        _logger = logger;
    }

    public string ProviderName => ScientificLiteratureSourceNames.EuropePmc;

    public async Task<SourceMaterialCandidate?> TryAcquireAsync(
        SourceMaterialStudyContext study,
        int maxContentCharacters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled || string.IsNullOrWhiteSpace(study.Pmcid))
        {
            return null;
        }

        var boundedContentCharacters = Math.Min(_options.BoundedMaxContentCharacters, maxContentCharacters);
        var uri = new Uri(_httpClient.BaseAddress ?? new Uri("https://www.ebi.ac.uk/europepmc/webservices/rest/"), $"{Uri.EscapeDataString(study.Pmcid)}/fullTextXML");
        var stopwatch = Stopwatch.StartNew();

        using var response = await SendWithRetryAsync(uri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
        {
            _logger.LogInformation(
                "EuropePmcFullTextUnavailable. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PMCID: {Pmcid}; HttpStatusCode: {HttpStatusCode}; DurationMs: {DurationMs}",
                study.ResearchRunId,
                study.StudyId,
                study.Pmcid,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return null;
        }

        var xml = await ReadBoundedContentAsync(response.Content, cancellationToken);
        var parsed = _parser.Parse(xml, boundedContentCharacters);
        stopwatch.Stop();

        _logger.LogInformation(
            "EuropePmcFullTextAcquired. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; PMCID: {Pmcid}; ContentLength: {ContentLength}; WasTruncated: {WasTruncated}; DurationMs: {DurationMs}",
            study.ResearchRunId,
            study.StudyId,
            study.Pmcid,
            parsed.Content.Length,
            parsed.WasTruncated,
            stopwatch.ElapsedMilliseconds);

        return new SourceMaterialCandidate(
            SourceMaterialType.StructuredFullText,
            ScientificLiteratureSourceNames.EuropePmc,
            study.Pmcid,
            "EuropePmcFullTextXml",
            parsed.Content,
            DateTimeOffset.UtcNow,
            null,
            parsed.License,
            parsed.LicenseUrl,
            SourceMaterialAccessStatus.OpenAccess,
            parsed.WasTruncated,
            parsed.SectionNames);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var retryCount = 0;; retryCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await SendOnceAsync(uri, cancellationToken);
            }
            catch (Exception exception) when (IsTransientFailure(exception) && retryCount < _options.BoundedMaxRetryAttempts)
            {
                var delay = ComputeRetryDelay(retryCount, exception);
                _logger.LogWarning(
                    exception,
                    "EuropePmcFullTextTransientRequestFailed. Attempt: {Attempt}; RetryNumber: {RetryNumber}; DelayMs: {DelayMs}",
                    retryCount + 1,
                    retryCount + 1,
                    delay.TotalMilliseconds);
                await _retryDelay.DelayAsync(delay, cancellationToken);
            }
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
        {
            return response;
        }

        var diagnosticBody = await ReadBoundedErrorBodyAsync(response.Content, cancellationToken);
        var retryAfter = ReadRetryAfter(response.Headers.RetryAfter);
        var exception = new EuropePmcFullTextHttpException(response.StatusCode, diagnosticBody, retryAfter);
        response.Dispose();
        throw exception;
    }

    private static bool IsTransientFailure(Exception exception)
    {
        return exception switch
        {
            EuropePmcFullTextHttpException { StatusCode: HttpStatusCode.TooManyRequests } => true,
            EuropePmcFullTextHttpException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            HttpRequestException { StatusCode: null } => true,
            TaskCanceledException => true,
            _ => false
        };
    }

    private TimeSpan ComputeRetryDelay(int retryCount, Exception exception)
    {
        if (exception is EuropePmcFullTextHttpException { RetryAfter: { } retryAfter })
        {
            return retryAfter <= MaximumRetryDelay ? retryAfter : MaximumRetryDelay;
        }

        var multiplier = Math.Pow(2, retryCount);
        var baseDelayMilliseconds = Math.Min(_options.RetryBaseDelay.TotalMilliseconds * multiplier, MaximumRetryDelay.TotalMilliseconds);
        var jitterCeiling = Math.Max(1, Math.Min(baseDelayMilliseconds * 0.25, 1_000));
        var jitterMilliseconds = Random.Shared.Next(0, (int)jitterCeiling + 1);
        return TimeSpan.FromMilliseconds(Math.Min(baseDelayMilliseconds + jitterMilliseconds, MaximumRetryDelay.TotalMilliseconds));
    }

    private static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value > TimeSpan.Zero ? retryAfter.Delta.Value : TimeSpan.Zero;
        }

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static async Task<string> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumXmlBytes)
            {
                throw new EuropePmcFullTextResponseException($"Europe PMC full-text XML exceeded {MaximumXmlBytes} bytes.");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static async Task<string?> ReadBoundedErrorBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumErrorBodyBytes];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

        return read == 0
            ? null
            : System.Text.Encoding.UTF8.GetString(buffer, 0, read).Trim();
    }
}

public sealed class EuropePmcFullTextHttpException : Exception
{
    public EuropePmcFullTextHttpException(HttpStatusCode statusCode, string? diagnosticBody, TimeSpan? retryAfter)
        : base($"Europe PMC full-text request failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        DiagnosticBody = diagnosticBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public string? DiagnosticBody { get; }

    public TimeSpan? RetryAfter { get; }
}

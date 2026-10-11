using System.Diagnostics;
using System.Globalization;
using System.Net;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.Crossref;

public interface ICrossrefRetryDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class CrossrefRetryDelay : ICrossrefRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class CrossrefScientificLiteratureSource(
    HttpClient httpClient,
    IOptions<CrossrefOptions> options,
    ICrossrefRequestGate requestGate,
    ICrossrefRetryDelay retryDelay,
    ILogger<CrossrefScientificLiteratureSource> logger,
    TimeProvider? timeProvider = null) : IScientificLiteratureSource
{
    private readonly CrossrefOptions _options = options.Value;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    public string SourceName => ScientificLiteratureSourceNames.Crossref;
    public string PrepareQuery(string query) => CrossrefQueryTranslator.Translate(query);

    public async Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled) throw Failure("Crossref source is disabled.", LiteratureProviderFailureCategory.ProviderProtocolError);
        var query = PrepareQuery(request.Query);
        var started = _clock.GetUtcNow();
        var candidates = new Dictionary<string, ScientificStudyCandidate>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var cursor = "*";
        var processedRecords = 0;
        try
        {
            // Bound inspected records and pages, not only accepted unique candidates.
            // Malformed/duplicate results must not trigger an unbounded corpus scan.
            while (processedRecords < _options.MaxResultsPerQuery && cursors.Add(cursor))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = Math.Min(_options.PageSize, _options.MaxResultsPerQuery - processedRecords);
                var stopwatch = Stopwatch.StartNew();
                var body = await SendWithRetryAsync(BuildUri(query, rows, cursor), cancellationToken);
                var page = CrossrefResponseMapper.Parse(body);
                cancellationToken.ThrowIfCancellationRequested();
                if (page.RawCount > rows) throw new CrossrefResponseException();
                processedRecords += page.RawCount;
                foreach (var candidate in page.Candidates) candidates.TryAdd(candidate.Doi!, candidate);
                logger.LogInformation(
                    "CrossrefSearchPageCompleted. ResearchRunId: {ResearchRunId}; SearchExecutionId: {SearchExecutionId}; InspectedRecords: {InspectedRecords}; CandidateCount: {CandidateCount}; DurationMs: {DurationMs}",
                    request.ResearchRunId, request.SearchExecutionId, processedRecords, candidates.Count, stopwatch.ElapsedMilliseconds);
                if (page.RawCount < rows || string.IsNullOrWhiteSpace(page.NextCursor)) break;
                cursor = page.NextCursor;
            }
            return new ScientificSearchResult(SourceName, started, candidates.Count, candidates.Values.ToArray());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("Crossref request timed out.", LiteratureProviderFailureCategory.Timeout);
        }
        catch (TimeoutException) { throw Failure("Crossref body read timed out.", LiteratureProviderFailureCategory.Timeout); }
        catch (OperationCanceledException) { throw; }
        catch (ProviderResponseTooLargeException) { throw Failure("Crossref response exceeded the byte limit.", LiteratureProviderFailureCategory.ResponseTooLarge); }
        catch (CrossrefResponseException) { throw Failure("Crossref returned an invalid response.", LiteratureProviderFailureCategory.InvalidResponse); }
        catch (CrossrefHttpException exception)
        {
            throw Failure($"Crossref request failed with HTTP {(int)exception.StatusCode}.",
                exception.StatusCode == HttpStatusCode.TooManyRequests ? LiteratureProviderFailureCategory.RateLimited : LiteratureProviderFailureCategory.ProviderProtocolError);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw Failure("Crossref network request failed.", LiteratureProviderFailureCategory.NetworkFailure);
        }
        catch (ScientificLiteratureRateLimitException) { throw Failure("Crossref local limiter rejected the request.", LiteratureProviderFailureCategory.RateLimited); }
    }

    private Uri BuildUri(string query, int rows, string cursor)
    {
        var parameters = new Dictionary<string, string>
        {
            ["query"] = query,
            ["filter"] = "type:journal-article,has-abstract:true",
            ["sort"] = "score",
            ["order"] = "desc",
            ["rows"] = rows.ToString(CultureInfo.InvariantCulture),
            ["cursor"] = cursor
        };
        if (!string.IsNullOrWhiteSpace(_options.Email)) parameters["mailto"] = _options.Email;
        return new UriBuilder(new Uri(httpClient.BaseAddress ?? new Uri(_options.BaseUrl), "works"))
        {
            Query = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"))
        }.Uri;
    }

    private async Task<string> SendWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var retry = 0;; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var permit = await requestGate.AcquireAsync(cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    if (response.Headers.RetryAfter?.Date is { } date) retryAfter = date - _clock.GetUtcNow();
                    throw new CrossrefHttpException(response.StatusCode, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
                }
                return await BoundedProviderBody.ReadAsync(response.Content, _options.MaxResponseBytes,
                    TimeSpan.FromSeconds(_options.BodyReadTimeoutSeconds), cancellationToken, _clock);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && retry < _options.MaxRetryAttempts && IsTransient(exception))
            {
                var delay = exception is CrossrefHttpException { RetryAfter: { } supplied } ? supplied :
                    TimeSpan.FromMilliseconds(Math.Min(30_000, _options.RetryBaseDelayMilliseconds * Math.Pow(2, retry) * (1 + Random.Shared.NextDouble() * 0.25)));
                logger.LogWarning("CrossrefTransientRequestFailed. Attempt: {Attempt}; DelayMs: {DelayMs}; HttpStatusCode: {HttpStatusCode}",
                    retry + 1, delay.TotalMilliseconds, exception is CrossrefHttpException http ? (int?)http.StatusCode : null);
                await retryDelay.DelayAsync(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        // A longer server cooldown is an explicit failure, not permission to retry early.
        CrossrefHttpException { RetryAfter: { } delay } when delay > MaximumRetryDelay => false,
        CrossrefHttpException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
        HttpRequestException { StatusCode: null } or IOException or TaskCanceledException or TimeoutException => true,
        _ => false
    };

    private static ScientificLiteratureSourceException Failure(string message, LiteratureProviderFailureCategory category) => new(message, category);
    private sealed class CrossrefHttpException(HttpStatusCode statusCode, TimeSpan? retryAfter) : Exception
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}

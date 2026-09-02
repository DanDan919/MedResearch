using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using MedResearch.Application.Research.Literature;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedScientificLiteratureSource : IScientificLiteratureSource
{
    public const string PubMedSourceName = "PubMed";
    private const int MaximumErrorBodyBytes = 1024;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly PubMedOptions _options;
    private readonly PubMedSearchResponseParser _searchResponseParser;
    private readonly PubMedArticleMapper _articleMapper;
    private readonly IPubMedRequestGate _requestGate;
    private readonly IPubMedRetryDelay _retryDelay;
    private readonly ILogger<PubMedScientificLiteratureSource> _logger;

    public PubMedScientificLiteratureSource(
        HttpClient httpClient,
        IOptions<PubMedOptions> options,
        PubMedSearchResponseParser searchResponseParser,
        PubMedArticleMapper articleMapper,
        IPubMedRequestGate requestGate,
        IPubMedRetryDelay retryDelay,
        ILogger<PubMedScientificLiteratureSource> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _searchResponseParser = searchResponseParser;
        _articleMapper = articleMapper;
        _requestGate = requestGate;
        _retryDelay = retryDelay;
        _logger = logger;
    }

    public string SourceName => PubMedSourceName;

    public async Task<ScientificSearchResult> SearchAsync(
        ScientificSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            throw new ScientificLiteratureSourceException("PubMed source is disabled by configuration.");
        }

        try
        {
            var searchedAt = DateTimeOffset.UtcNow;
            var searchResult = await SearchPmidsAsync(request.Query, cancellationToken);
            var pmids = searchResult.Pmids.Distinct(StringComparer.Ordinal).ToArray();

            _logger.LogInformation(
                "PubMedESearchCompleted. ResearchRunId: {ResearchRunId}; SearchExecutionId: {SearchExecutionId}; ReturnedPmidCount: {ReturnedPmidCount}; TotalAvailableCount: {TotalAvailableCount}",
                request.ResearchRunId,
                request.SearchExecutionId,
                pmids.Length,
                searchResult.TotalAvailableCount);

            if (pmids.Length == 0)
            {
                return new ScientificSearchResult(SourceName, searchedAt, 0, []);
            }

            var candidates = await FetchStudyCandidatesAsync(request, pmids, cancellationToken);

            return new ScientificSearchResult(SourceName, searchedAt, pmids.Length, candidates);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScientificLiteratureSourceException("PubMed request timed out.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PubMedResponseException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed returned an invalid response.", exception);
        }
        catch (PubMedHttpException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ScientificLiteratureSourceException("PubMed rate limit was reached.", exception);
        }
        catch (PubMedHttpException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed request failed.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed request failed.", exception);
        }
        catch (ScientificLiteratureRateLimitException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed local rate limiter rejected the request.", exception);
        }
    }

    private async Task<PubMedSearchResult> SearchPmidsAsync(string query, CancellationToken cancellationToken)
    {
        var uri = BuildUri("esearch.fcgi", new Dictionary<string, string?>
        {
            ["db"] = "pubmed",
            ["term"] = query,
            ["retmax"] = _options.BoundedMaxResultsPerQuery.ToString(CultureInfo.InvariantCulture),
            ["retmode"] = "json",
            ["sort"] = "relevance"
        });

        using var response = await SendWithRetryAsync(uri, "ESearch", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return _searchResponseParser.Parse(content);
    }

    private async Task<IReadOnlyCollection<ScientificStudyCandidate>> FetchStudyCandidatesAsync(
        ScientificSearchRequest request,
        IReadOnlyList<string> pmids,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ScientificStudyCandidate>();
        var batches = pmids
            .Distinct(StringComparer.Ordinal)
            .Chunk(_options.BoundedFetchBatchSize)
            .ToArray();

        for (var index = 0; index < batches.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = batches[index];
            var uri = BuildUri("efetch.fcgi", new Dictionary<string, string?>
            {
                ["db"] = "pubmed",
                ["id"] = string.Join(',', batch),
                ["retmode"] = "xml"
            });

            var stopwatch = Stopwatch.StartNew();
            using var response = await SendWithRetryAsync(uri, "EFetch", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            candidates.AddRange(_articleMapper.MapArticles(content));
            stopwatch.Stop();

            _logger.LogInformation(
                "PubMedEFetchBatchCompleted. ResearchRunId: {ResearchRunId}; SearchExecutionId: {SearchExecutionId}; BatchNumber: {BatchNumber}; BatchCount: {BatchCount}; BatchSize: {BatchSize}; DurationMs: {DurationMs}",
                request.ResearchRunId,
                request.SearchExecutionId,
                index + 1,
                batches.Length,
                batch.Length,
                stopwatch.ElapsedMilliseconds);
        }

        return DeduplicateCandidates(candidates);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri uri,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var retryCount = 0;; retryCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = retryCount + 1;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await SendOnceAsync(uri, cancellationToken);
                stopwatch.Stop();

                _logger.LogDebug(
                    "PubMedRequestSucceeded. Operation: {Operation}; Attempt: {Attempt}; DurationMs: {DurationMs}",
                    operation,
                    attempt,
                    stopwatch.ElapsedMilliseconds);

                return response;
            }
            catch (Exception exception) when (IsTransientFailure(exception) && retryCount < _options.BoundedMaxRetryAttempts)
            {
                stopwatch.Stop();
                var delay = ComputeRetryDelay(retryCount, exception);

                _logger.LogWarning(
                    exception,
                    "PubMedTransientRequestFailed. Operation: {Operation}; Attempt: {Attempt}; RetryNumber: {RetryNumber}; DelayMs: {DelayMs}; DurationMs: {DurationMs}; HttpStatusCode: {HttpStatusCode}; DiagnosticBody: {DiagnosticBody}",
                    operation,
                    attempt,
                    retryCount + 1,
                    delay.TotalMilliseconds,
                    stopwatch.ElapsedMilliseconds,
                    exception is PubMedHttpException httpException ? (int?)httpException.StatusCode : null,
                    exception is PubMedHttpException bodyException ? bodyException.DiagnosticBody : null);

                await _retryDelay.DelayAsync(delay, cancellationToken);
            }
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var diagnosticBody = await ReadBoundedErrorBodyAsync(response.Content, cancellationToken);
        var retryAfter = ReadRetryAfter(response.Headers.RetryAfter);
        var exception = new PubMedHttpException(response.StatusCode, diagnosticBody, retryAfter);
        response.Dispose();
        throw exception;
    }

    private Uri BuildUri(string endpoint, IReadOnlyDictionary<string, string?> parameters)
    {
        var allParameters = new Dictionary<string, string?>(parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["tool"] = _options.Tool,
            ["email"] = _options.Email,
            ["api_key"] = _options.ApiKey
        };

        var query = string.Join(
            '&',
            allParameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                .Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!)}"));

        var baseUri = _httpClient.BaseAddress ?? new Uri(_options.BaseUrl, UriKind.Absolute);
        var endpointUri = new Uri(baseUri, endpoint);
        var builder = new UriBuilder(endpointUri) { Query = query };

        _logger.LogDebug(
            "Prepared PubMed E-utilities request. Endpoint: {Endpoint}; MaxResultsPerQuery: {MaxResultsPerQuery}; FetchBatchSize: {FetchBatchSize}; MaxRequestsPerSecond: {MaxRequestsPerSecond}; HasApiKey: {HasApiKey}; HasEmail: {HasEmail}",
            endpoint,
            _options.BoundedMaxResultsPerQuery,
            _options.BoundedFetchBatchSize,
            _options.MaxRequestsPerSecond,
            _options.HasApiKey,
            !string.IsNullOrWhiteSpace(_options.Email));

        return builder.Uri;
    }

    private static bool IsTransientFailure(Exception exception)
    {
        return exception switch
        {
            PubMedHttpException { StatusCode: HttpStatusCode.TooManyRequests } => true,
            PubMedHttpException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            HttpRequestException { StatusCode: null } => true,
            TaskCanceledException => true,
            _ => false
        };
    }

    private TimeSpan ComputeRetryDelay(int retryCount, Exception exception)
    {
        if (exception is PubMedHttpException { RetryAfter: { } retryAfter })
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

    private static async Task<string?> ReadBoundedErrorBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumErrorBodyBytes];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

        if (read == 0)
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, read).Trim();
    }

    private static IReadOnlyCollection<ScientificStudyCandidate> DeduplicateCandidates(
        IEnumerable<ScientificStudyCandidate> candidates)
    {
        var deduplicated = new List<ScientificStudyCandidate>();
        var seenPmids = new HashSet<string>(StringComparer.Ordinal);
        var seenDois = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Pmid))
            {
                if (!seenPmids.Add(candidate.Pmid))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidate.Doi))
                {
                    seenDois.Add(candidate.Doi);
                }

                deduplicated.Add(candidate);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Doi))
            {
                if (seenDois.Add(candidate.Doi))
                {
                    deduplicated.Add(candidate);
                }
            }
        }

        return deduplicated;
    }
}
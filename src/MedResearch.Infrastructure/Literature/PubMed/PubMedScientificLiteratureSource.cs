using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedScientificLiteratureSource : IScientificLiteratureSource
{
    public const string PubMedSourceName = ScientificLiteratureSourceNames.PubMed;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly PubMedOptions _options;
    private readonly PubMedSearchResponseParser _searchResponseParser;
    private readonly PubMedArticleMapper _articleMapper;
    private readonly IPubMedRequestGate _requestGate;
    private readonly IPubMedRetryDelay _retryDelay;
    private readonly ILogger<PubMedScientificLiteratureSource> _logger;
    private readonly TimeProvider _timeProvider;

    public PubMedScientificLiteratureSource(
        HttpClient httpClient,
        IOptions<PubMedOptions> options,
        PubMedSearchResponseParser searchResponseParser,
        PubMedArticleMapper articleMapper,
        IPubMedRequestGate requestGate,
        IPubMedRetryDelay retryDelay,
        ILogger<PubMedScientificLiteratureSource> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _searchResponseParser = searchResponseParser;
        _articleMapper = articleMapper;
        _requestGate = requestGate;
        _retryDelay = retryDelay;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            var pmids = searchResult.Pmids.Distinct(StringComparer.Ordinal).Take(_options.BoundedMaxResultsPerQuery).ToArray();

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
            throw new ScientificLiteratureSourceException("PubMed request timed out.", LiteratureProviderFailureCategory.Timeout, exception);
        }
        catch (TimeoutException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed body read timed out.", LiteratureProviderFailureCategory.Timeout, exception);
        }
        catch (ProviderResponseTooLargeException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed response exceeded the byte limit.", LiteratureProviderFailureCategory.ResponseTooLarge, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PubMedResponseException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed returned an invalid response.", LiteratureProviderFailureCategory.InvalidResponse, exception);
        }
        catch (PubMedHttpException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ScientificLiteratureSourceException("PubMed rate limit was reached.", LiteratureProviderFailureCategory.RateLimited, exception);
        }
        catch (PubMedHttpException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed request failed.", LiteratureProviderFailureCategory.ProviderProtocolError, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ScientificLiteratureSourceException("PubMed request failed.", LiteratureProviderFailureCategory.NetworkFailure);
        }
        catch (ScientificLiteratureRateLimitException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed local rate limiter rejected the request.", LiteratureProviderFailureCategory.RateLimited, exception);
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

        var content = await SendWithRetryAsync(uri, "ESearch", _options.MaxSearchResponseBytes, cancellationToken);

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
            var content = await SendWithRetryAsync(uri, "EFetch", _options.MaxFetchResponseBytes, cancellationToken);
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

    private async Task<string> SendWithRetryAsync(
        Uri uri,
        string operation,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        for (var retryCount = 0;; retryCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = retryCount + 1;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var response = await SendOnceAsync(uri, cancellationToken);
                var body = await BoundedProviderBody.ReadAsync(response.Content, maximumBytes, TimeSpan.FromSeconds(_options.BodyReadTimeoutSeconds), cancellationToken, _timeProvider);
                stopwatch.Stop();

                _logger.LogDebug(
                    "PubMedRequestSucceeded. Operation: {Operation}; Attempt: {Attempt}; DurationMs: {DurationMs}",
                    operation,
                    attempt,
                    stopwatch.ElapsedMilliseconds);

                return body;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransientFailure(exception) && retryCount < _options.BoundedMaxRetryAttempts)
            {
                stopwatch.Stop();
                var delay = ComputeRetryDelay(retryCount, exception);

                _logger.LogWarning(
                    "PubMedTransientRequestFailed. Operation: {Operation}; Attempt: {Attempt}; RetryNumber: {RetryNumber}; DelayMs: {DelayMs}; DurationMs: {DurationMs}; HttpStatusCode: {HttpStatusCode}",
                    operation,
                    attempt,
                    retryCount + 1,
                    delay.TotalMilliseconds,
                    stopwatch.ElapsedMilliseconds,
                    exception is PubMedHttpException httpException ? (int?)httpException.StatusCode : null);

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

        using (response)
        {
            throw new PubMedHttpException(response.StatusCode, null, ReadRetryAfter(response.Headers.RetryAfter));
        }
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
            IOException => true,
            TaskCanceledException or TimeoutException => true,
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

    private static IReadOnlyCollection<ScientificStudyCandidate> DeduplicateCandidates(
        IEnumerable<ScientificStudyCandidate> candidates)
    {
        var deduplicated = new List<ScientificStudyCandidate>();
        var seenPmids = new HashSet<string>(StringComparer.Ordinal);
        var seenPmcids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

using System.Net;
using MedResearch.Application.Research.Literature;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.PubMed;

public sealed class PubMedScientificLiteratureSource : IScientificLiteratureSource
{
    public const string PubMedSourceName = "PubMed";

    private readonly HttpClient _httpClient;
    private readonly PubMedOptions _options;
    private readonly PubMedSearchResponseParser _searchResponseParser;
    private readonly PubMedArticleMapper _articleMapper;
    private readonly ILogger<PubMedScientificLiteratureSource> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public PubMedScientificLiteratureSource(
        HttpClient httpClient,
        IOptions<PubMedOptions> options,
        PubMedSearchResponseParser searchResponseParser,
        PubMedArticleMapper articleMapper,
        ILogger<PubMedScientificLiteratureSource> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _searchResponseParser = searchResponseParser;
        _articleMapper = articleMapper;
        _logger = logger;
    }

    public string SourceName => PubMedSourceName;

    public async Task<ScientificSearchResult> SearchAsync(
        ScientificSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        try
        {
            var searchedAt = DateTimeOffset.UtcNow;
            var pmids = await SearchPmidsAsync(request.Query, cancellationToken);

            if (pmids.Count == 0)
            {
                return new ScientificSearchResult(SourceName, searchedAt, 0, []);
            }

            var candidates = await FetchStudyCandidatesAsync(pmids, cancellationToken);

            return new ScientificSearchResult(SourceName, searchedAt, pmids.Count, candidates);
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
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ScientificLiteratureSourceException("PubMed rate limit was reached.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ScientificLiteratureSourceException("PubMed request failed.", exception);
        }
    }

    private async Task<IReadOnlyList<string>> SearchPmidsAsync(string query, CancellationToken cancellationToken)
    {
        var uri = BuildUri("esearch.fcgi", new Dictionary<string, string?>
        {
            ["db"] = "pubmed",
            ["term"] = query,
            ["retmax"] = _options.BoundedResultLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["retmode"] = "json",
            ["sort"] = "relevance"
        });

        using var response = await SendAsync(uri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return _searchResponseParser.ParsePmids(content);
    }

    private async Task<IReadOnlyCollection<ScientificStudyCandidate>> FetchStudyCandidatesAsync(
        IReadOnlyList<string> pmids,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri("efetch.fcgi", new Dictionary<string, string?>
        {
            ["db"] = "pubmed",
            ["id"] = string.Join(',', pmids),
            ["retmode"] = "xml"
        });

        using var response = await SendAsync(uri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return _articleMapper.MapArticles(content);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_nextRequestAt > now)
            {
                await Task.Delay(_nextRequestAt - now, cancellationToken);
            }

            _nextRequestAt = DateTimeOffset.UtcNow.Add(_options.RequestInterval);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException("PubMed rate limit response received.", null, response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"PubMed returned HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            return response;
        }
        finally
        {
            _requestGate.Release();
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
            "Prepared PubMed E-utilities request. Endpoint: {Endpoint}; ResultLimit: {ResultLimit}; HasApiKey: {HasApiKey}; HasEmail: {HasEmail}",
            endpoint,
            _options.BoundedResultLimit,
            !string.IsNullOrWhiteSpace(_options.ApiKey),
            !string.IsNullOrWhiteSpace(_options.Email));

        return builder.Uri;
    }
}

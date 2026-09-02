using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.Literature;
using MedResearch.Infrastructure.Literature.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.Infrastructure.Literature.EuropePmc;

public sealed class EuropePmcScientificLiteratureSource : IScientificLiteratureSource
{
    public const string EuropePmcSourceName = ScientificLiteratureSourceNames.EuropePmc;
    private const int MaximumErrorBodyBytes = 1024;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly EuropePmcOptions _options;
    private readonly IEuropePmcRequestGate _requestGate;
    private readonly IEuropePmcRetryDelay _retryDelay;
    private readonly ILogger<EuropePmcScientificLiteratureSource> _logger;

    public EuropePmcScientificLiteratureSource(
        HttpClient httpClient,
        IOptions<EuropePmcOptions> options,
        IEuropePmcRequestGate requestGate,
        IEuropePmcRetryDelay retryDelay,
        ILogger<EuropePmcScientificLiteratureSource> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _requestGate = requestGate;
        _retryDelay = retryDelay;
        _logger = logger;
    }

    public string SourceName => EuropePmcSourceName;

    public async Task<ScientificSearchResult> SearchAsync(
        ScientificSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            throw new ScientificLiteratureSourceException("Europe PMC source is disabled by configuration.");
        }

        try
        {
            var searchedAt = DateTimeOffset.UtcNow;
            var candidates = new List<ScientificStudyCandidate>();
            string? cursorMark = "*";
            var pageNumber = 0;

            while (candidates.Count < _options.BoundedMaxResultsPerQuery && !string.IsNullOrWhiteSpace(cursorMark))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pageNumber++;
                var remaining = _options.BoundedMaxResultsPerQuery - candidates.Count;
                var pageSize = Math.Min(_options.BoundedPageSize, remaining);
                var uri = BuildUri("search", new Dictionary<string, string?>
                {
                    ["query"] = request.Query,
                    ["format"] = "json",
                    ["resultType"] = "core",
                    ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture),
                    ["cursorMark"] = cursorMark
                });

                var stopwatch = Stopwatch.StartNew();
                using var response = await SendWithRetryAsync(uri, "Search", cancellationToken);
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var page = await ParseSearchPageAsync(stream, cancellationToken);
                stopwatch.Stop();

                candidates.AddRange(page.Candidates);

                _logger.LogInformation(
                    "EuropePmcSearchPageCompleted. ResearchRunId: {ResearchRunId}; SearchExecutionId: {SearchExecutionId}; PageNumber: {PageNumber}; PageSize: {PageSize}; ReturnedCount: {ReturnedCount}; HitCount: {HitCount}; DurationMs: {DurationMs}",
                    request.ResearchRunId,
                    request.SearchExecutionId,
                    pageNumber,
                    pageSize,
                    page.Candidates.Count,
                    page.HitCount,
                    stopwatch.ElapsedMilliseconds);

                if (page.Candidates.Count == 0 || string.IsNullOrWhiteSpace(page.NextCursorMark) || string.Equals(page.NextCursorMark, cursorMark, StringComparison.Ordinal))
                {
                    break;
                }

                cursorMark = page.NextCursorMark;
            }

            var deduplicated = DeduplicateCandidates(candidates)
                .Take(_options.BoundedMaxResultsPerQuery)
                .ToArray();

            return new ScientificSearchResult(SourceName, searchedAt, deduplicated.Length, deduplicated);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScientificLiteratureSourceException("Europe PMC request timed out.", exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EuropePmcResponseException exception)
        {
            throw new ScientificLiteratureSourceException("Europe PMC returned an invalid response.", exception);
        }
        catch (EuropePmcHttpException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ScientificLiteratureSourceException("Europe PMC rate limit was reached.", exception);
        }
        catch (EuropePmcHttpException exception)
        {
            throw new ScientificLiteratureSourceException("Europe PMC request failed.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ScientificLiteratureSourceException("Europe PMC request failed.", exception);
        }
        catch (ScientificLiteratureRateLimitException exception)
        {
            throw new ScientificLiteratureSourceException("Europe PMC local rate limiter rejected the request.", exception);
        }
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
                    "EuropePmcRequestSucceeded. Operation: {Operation}; Attempt: {Attempt}; DurationMs: {DurationMs}",
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
                    "EuropePmcTransientRequestFailed. Operation: {Operation}; Attempt: {Attempt}; RetryNumber: {RetryNumber}; DelayMs: {DelayMs}; DurationMs: {DurationMs}; HttpStatusCode: {HttpStatusCode}; DiagnosticBody: {DiagnosticBody}",
                    operation,
                    attempt,
                    retryCount + 1,
                    delay.TotalMilliseconds,
                    stopwatch.ElapsedMilliseconds,
                    exception is EuropePmcHttpException httpException ? (int?)httpException.StatusCode : null,
                    exception is EuropePmcHttpException bodyException ? bodyException.DiagnosticBody : null);

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
        var exception = new EuropePmcHttpException(response.StatusCode, diagnosticBody, retryAfter);
        response.Dispose();
        throw exception;
    }

    private Uri BuildUri(string endpoint, IReadOnlyDictionary<string, string?> parameters)
    {
        var query = string.Join(
            '&',
            parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                .Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!)}"));

        var baseUri = _httpClient.BaseAddress ?? new Uri(_options.BaseUrl, UriKind.Absolute);
        var endpointUri = new Uri(baseUri, endpoint);
        return new UriBuilder(endpointUri) { Query = query }.Uri;
    }

    private static async Task<EuropePmcSearchPage> ParseSearchPageAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var hitCount = ReadInt(root, "hitCount") ?? 0;
            var nextCursorMark = ReadString(root, "nextCursorMark");
            var candidates = new List<ScientificStudyCandidate>();

            if (root.TryGetProperty("resultList", out var resultList) &&
                resultList.TryGetProperty("result", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    var candidate = MapResult(result);
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            return new EuropePmcSearchPage(hitCount, nextCursorMark, candidates);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new EuropePmcResponseException("Europe PMC search response could not be parsed as JSON.", exception);
        }
    }

    private static ScientificStudyCandidate? MapResult(JsonElement result)
    {
        var providerId = ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "id"));
        var providerSource = ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "source"));
        var pmid = ScientificIdentifierNormalizer.NormalizePmid(ReadString(result, "pmid"));
        var pmcid = ScientificIdentifierNormalizer.NormalizePmcid(ReadString(result, "pmcid"));
        var doi = ScientificIdentifierNormalizer.NormalizeDoi(ReadString(result, "doi"));
        var title = ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "title"));

        if (string.IsNullOrWhiteSpace(title) || (pmid is null && pmcid is null && doi is null))
        {
            return null;
        }

        var publicationParts = ReadPublicationParts(result);
        var authors = ReadAuthors(result);
        var publicationTypes = ReadPublicationTypes(result);
        var sourceIdentifier = providerSource is null || providerId is null
            ? providerId
            : $"{providerSource}:{providerId}";

        return new ScientificStudyCandidate(
            pmid,
            pmcid,
            doi,
            title,
            ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "abstractText")),
            ReadJournal(result),
            publicationParts.PublicationDate,
            publicationParts.Year,
            publicationParts.Month,
            publicationParts.Day,
            publicationTypes,
            authors,
            sourceIdentifier,
            EuropePmcSourceName);
    }

    private static PublicationParts ReadPublicationParts(JsonElement result)
    {
        foreach (var propertyName in new[] { "firstPublicationDate", "firstIndexDate" })
        {
            var parts = ParseDate(ReadString(result, propertyName));
            if (parts.HasAnyPart)
            {
                return parts;
            }
        }

        if (result.TryGetProperty("journalInfo", out var journalInfo))
        {
            foreach (var propertyName in new[] { "printPublicationDate", "electronicPublicationDate" })
            {
                var parts = ParseDate(ReadString(journalInfo, propertyName));
                if (parts.HasAnyPart)
                {
                    return parts;
                }
            }
        }

        return BuildPublicationParts(ReadInt(result, "pubYear"), null, null);
    }

    private static PublicationParts ParseDate(string? value)
    {
        var normalized = ScientificIdentifierNormalizer.NormalizeWhitespace(value);
        if (normalized is null)
        {
            return new PublicationParts(null, null, null, null);
        }

        var tokens = normalized.Split(['-', '/', ' '], StringSplitOptions.RemoveEmptyEntries);
        var year = tokens.Length >= 1 ? ParseInt(tokens[0]) : null;
        var month = tokens.Length >= 2 ? ParseMonth(tokens[1]) : null;
        var day = tokens.Length >= 3 ? ParseInt(tokens[2]) : null;
        return BuildPublicationParts(year, month, day);
    }

    private static PublicationParts BuildPublicationParts(int? year, int? month, int? day)
    {
        DateOnly? publicationDate = null;
        if (year.HasValue && month.HasValue && day.HasValue &&
            DateOnly.TryParse($"{year.Value:0000}-{month.Value:00}-{day.Value:00}", out var parsedDate))
        {
            publicationDate = parsedDate;
        }

        return new PublicationParts(publicationDate, year, month, day);
    }

    private static IReadOnlyCollection<string> ReadAuthors(JsonElement result)
    {
        if (result.TryGetProperty("authorList", out var authorList) &&
            authorList.TryGetProperty("author", out var authors) &&
            authors.ValueKind == JsonValueKind.Array)
        {
            return authors
                .EnumerateArray()
                .Select(author => ReadString(author, "fullName") ?? ReadString(author, "collectiveName"))
                .Select(ScientificIdentifierNormalizer.NormalizeWhitespace)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var authorString = ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "authorString"));
        return authorString is null
            ? []
            : authorString.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static IReadOnlyCollection<string> ReadPublicationTypes(JsonElement result)
    {
        if (!result.TryGetProperty("pubTypeList", out var publicationTypeList) ||
            !publicationTypeList.TryGetProperty("pubType", out var publicationTypes) ||
            publicationTypes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return publicationTypes
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Select(ScientificIdentifierNormalizer.NormalizeWhitespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadJournal(JsonElement result)
    {
        var journalTitle = ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(result, "journalTitle"));
        if (journalTitle is not null)
        {
            return journalTitle;
        }

        if (result.TryGetProperty("journalInfo", out var journalInfo) &&
            journalInfo.TryGetProperty("journal", out var journal))
        {
            return ScientificIdentifierNormalizer.NormalizeWhitespace(ReadString(journal, "title") ?? ReadString(journal, "isoabbreviation"));
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseMonth(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericMonth))
        {
            return numericMonth is >= 1 and <= 12 ? numericMonth : null;
        }

        var abbreviated = value.Length >= 3 ? value[..3] : value;
        return abbreviated.ToLowerInvariant() switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool IsTransientFailure(Exception exception)
    {
        return exception switch
        {
            EuropePmcHttpException { StatusCode: HttpStatusCode.TooManyRequests } => true,
            EuropePmcHttpException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            HttpRequestException { StatusCode: null } => true,
            TaskCanceledException => true,
            _ => false
        };
    }

    private TimeSpan ComputeRetryDelay(int retryCount, Exception exception)
    {
        if (exception is EuropePmcHttpException { RetryAfter: { } retryAfter })
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

    private static IReadOnlyCollection<ScientificStudyCandidate> DeduplicateCandidates(IEnumerable<ScientificStudyCandidate> candidates)
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

                if (!string.IsNullOrWhiteSpace(candidate.Pmcid))
                {
                    seenPmcids.Add(candidate.Pmcid);
                }

                if (!string.IsNullOrWhiteSpace(candidate.Doi))
                {
                    seenDois.Add(candidate.Doi);
                }

                deduplicated.Add(candidate);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Pmcid))
            {
                if (!seenPmcids.Add(candidate.Pmcid))
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

            if (!string.IsNullOrWhiteSpace(candidate.Doi) && seenDois.Add(candidate.Doi))
            {
                deduplicated.Add(candidate);
            }
        }

        return deduplicated;
    }

    private sealed record EuropePmcSearchPage(
        int HitCount,
        string? NextCursorMark,
        IReadOnlyCollection<ScientificStudyCandidate> Candidates);

    private sealed record PublicationParts(DateOnly? PublicationDate, int? Year, int? Month, int? Day)
    {
        public bool HasAnyPart => PublicationDate.HasValue || Year.HasValue || Month.HasValue || Day.HasValue;
    }
}

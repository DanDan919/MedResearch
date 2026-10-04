using MedResearch.Domain;
using MedResearch.Application.Security;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research;

public sealed class ListResearchRunsUseCase
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IResearchStore _researchStore;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<ListResearchRunsUseCase> _logger;

    public ListResearchRunsUseCase(
        IResearchStore researchStore,
        ICurrentActor currentActor,
        ILogger<ListResearchRunsUseCase> logger)
    {
        _researchStore = researchStore;
        _currentActor = currentActor;
        _logger = logger;
    }

    public async Task<ResearchRunListResult> ExecuteAsync(
        ListResearchRunsQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? DefaultPage;
        var pageSize = query.PageSize ?? DefaultPageSize;

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page must be greater than or equal to 1.");
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page size must be greater than or equal to 1.");
        }

        if (pageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query), $"Page size cannot exceed {MaxPageSize}.");
        }

        var status = ParseStatus(query.Status);
        var result = await _researchStore.ListResearchRunsAsync(
            page,
            pageSize,
            status,
            _currentActor.RequireSubjectId(),
            cancellationToken);

        _logger.LogInformation(
            "Research runs listed. Page: {Page}; PageSize: {PageSize}; TotalCount: {TotalCount}; Status: {Status}",
            result.Page,
            result.PageSize,
            result.TotalCount,
            status?.ToString() ?? "All");

        return result;
    }

    private static ResearchRunStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<ResearchRunStatus>(value.Trim(), ignoreCase: false, out var status)
            && Enum.IsDefined(status))
        {
            return status;
        }

        throw new ArgumentException($"Research run status '{value}' is not valid.", nameof(value));
    }
}

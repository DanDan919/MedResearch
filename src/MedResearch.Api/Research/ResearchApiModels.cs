namespace MedResearch.Api.Research;

public sealed record CreateResearchRequest(string? Question);

public sealed record CreateResearchResponse(Guid ResearchRunId, string Status);

public sealed record ResearchRunResponse(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

namespace MedResearch.Application.Research;

public sealed record CreateResearchCommand(string? Question);

public sealed record CreateResearchResult(Guid ResearchRunId, string Status);

public sealed record ResearchRunDetails(
    Guid ResearchRunId,
    string Question,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

namespace MedResearch.Application.Research.Literature;

public sealed record ScientificSearchRequest(Guid ResearchRunId, Guid SearchExecutionId, string Query);

public sealed record ScientificSearchResult(
    string Source,
    DateTimeOffset SearchedAt,
    int ReturnedResultCount,
    IReadOnlyCollection<ScientificStudyCandidate> Candidates);

public sealed record ScientificStudyCandidate(
    string? Pmid,
    string? Pmcid,
    string? Doi,
    string Title,
    string? Abstract,
    string? Journal,
    DateOnly? PublicationDate,
    int? PublicationYear,
    int? PublicationMonth,
    int? PublicationDay,
    IReadOnlyCollection<string> PublicationTypes,
    IReadOnlyCollection<string> Authors,
    string? ProviderRecordId,
    string Source);

public sealed record ScientificSearchPersistenceRequest(
    Guid SearchExecutionId,
    Guid ResearchRunId,
    Guid? ResearchPlanId,
    string Source,
    string Query,
    DateTimeOffset SearchedAt,
    int ResultCount,
    IReadOnlyCollection<ScientificStudyCandidate> Candidates);

public sealed record ScientificSearchPersistenceResult(
    Guid SearchExecutionId,
    int PersistedCount,
    int DuplicateCount);

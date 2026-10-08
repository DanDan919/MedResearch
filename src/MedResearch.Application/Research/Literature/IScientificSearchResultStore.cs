namespace MedResearch.Application.Research.Literature;

public interface IScientificSearchResultStore
{
    Task BeginAttemptAsync(Guid attemptId, Guid researchRunId, Guid researchPlanId, string source, string query, DateTimeOffset startedAt, CancellationToken cancellationToken);

    Task FailAttemptAsync(Guid attemptId, MedResearch.Domain.LiteratureProviderFailureCategory category, DateTimeOffset completedAt, CancellationToken cancellationToken);

    Task<bool> HasPersistedSearchAsync(
        Guid researchRunId,
        Guid researchPlanId,
        string source,
        string query,
        CancellationToken cancellationToken);

    Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
        ScientificSearchPersistenceRequest request,
        CancellationToken cancellationToken);
}

namespace MedResearch.Application.Research.Literature;

public interface IScientificSearchResultStore
{
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

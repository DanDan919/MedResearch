namespace MedResearch.Application.Research.Literature;

public interface IScientificSearchResultStore
{
    Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
        ScientificSearchPersistenceRequest request,
        CancellationToken cancellationToken);
}

namespace MedResearch.Application.Research.Literature;

public interface IScientificLiteratureSource
{
    string SourceName { get; }

    // Adapters own dialect conversion; the returned query is the execution/provenance key.
    string PrepareQuery(string query) => query;

    Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken);
}

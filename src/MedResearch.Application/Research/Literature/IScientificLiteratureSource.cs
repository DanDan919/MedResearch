namespace MedResearch.Application.Research.Literature;

public interface IScientificLiteratureSource
{
    string SourceName { get; }

    Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken);
}

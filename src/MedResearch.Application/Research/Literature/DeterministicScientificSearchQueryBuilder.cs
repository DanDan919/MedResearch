namespace MedResearch.Application.Research.Literature;

public sealed class DeterministicScientificSearchQueryBuilder : IScientificSearchQueryBuilder
{
    private const int MaximumQueryLength = 300;

    public string BuildQuery(string researchQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchQuestion);

        var query = string.Join(' ', researchQuestion.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));

        return query.Length <= MaximumQueryLength
            ? query
            : query[..MaximumQueryLength];
    }
}

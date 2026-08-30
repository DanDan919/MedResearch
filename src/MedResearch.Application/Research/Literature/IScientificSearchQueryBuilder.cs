namespace MedResearch.Application.Research.Literature;

public interface IScientificSearchQueryBuilder
{
    string BuildQuery(string researchQuestion);
}

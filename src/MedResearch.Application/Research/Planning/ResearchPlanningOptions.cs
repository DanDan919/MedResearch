namespace MedResearch.Application.Research.Planning;

public sealed class ResearchPlanningOptions
{
    public const string SectionName = "ResearchPlanning";
    public const int MaximumAllowedSearchQueries = ResearchPlanValidator.MaximumSearchQueryCount;

    public int MaxSearchQueries { get; init; } = MaximumAllowedSearchQueries;

    public int BoundedMaxSearchQueries => Math.Clamp(MaxSearchQueries, 1, MaximumAllowedSearchQueries);

    public void Validate()
    {
        if (MaxSearchQueries is < 1 or > MaximumAllowedSearchQueries)
        {
            throw new InvalidOperationException(
                $"ResearchPlanning:MaxSearchQueries must be between 1 and {MaximumAllowedSearchQueries}.");
        }
    }
}

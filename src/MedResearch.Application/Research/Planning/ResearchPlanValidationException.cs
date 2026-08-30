namespace MedResearch.Application.Research.Planning;

public sealed class ResearchPlanValidationException : Exception
{
    public ResearchPlanValidationException(string message)
        : base(message)
    {
    }
}

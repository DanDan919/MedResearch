namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchSynthesisValidationException : Exception
{
    public ResearchSynthesisValidationException(string message)
        : base(message)
    {
    }
}
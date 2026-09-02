namespace MedResearch.Infrastructure.Literature;

public sealed class ScientificLiteratureRateLimitException : Exception
{
    public ScientificLiteratureRateLimitException(string message)
        : base(message)
    {
    }
}

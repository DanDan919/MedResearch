namespace MedResearch.Application.Research.Literature;

public sealed class ScientificLiteratureSourceException : Exception
{
    public ScientificLiteratureSourceException(string message)
        : base(message)
    {
    }

    public ScientificLiteratureSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

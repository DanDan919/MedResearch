namespace MedResearch.Application.Research.Ai;

public class StructuredLlmException : Exception
{
    public StructuredLlmException(string message)
        : base(message)
    {
    }

    public StructuredLlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

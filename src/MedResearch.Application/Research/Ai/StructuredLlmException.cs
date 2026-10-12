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

public sealed class StructuredLlmTimeoutException : StructuredLlmException
{
    public StructuredLlmTimeoutException() : base("Bounded structured generation timed out.") { }
}

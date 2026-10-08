namespace MedResearch.Application.Research.Literature;

public sealed class ScientificLiteratureSourceException : Exception
{
    public MedResearch.Domain.LiteratureProviderFailureCategory FailureCategory { get; }

    public ScientificLiteratureSourceException(string message, MedResearch.Domain.LiteratureProviderFailureCategory category, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCategory = category;
    }

    public ScientificLiteratureSourceException(string message)
        : base(message)
    {
        FailureCategory = MedResearch.Domain.LiteratureProviderFailureCategory.UnexpectedFailure;
    }

    public ScientificLiteratureSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
        FailureCategory = MedResearch.Domain.LiteratureProviderFailureCategory.UnexpectedFailure;
    }
}

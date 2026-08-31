namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractionValidationException : Exception
{
    public EvidenceExtractionValidationException(string message)
        : base(message)
    {
    }

    public EvidenceExtractionValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceGroundingValidationException : Exception
{
    public EvidenceGroundingValidationException(string message)
        : base(message)
    {
    }
}

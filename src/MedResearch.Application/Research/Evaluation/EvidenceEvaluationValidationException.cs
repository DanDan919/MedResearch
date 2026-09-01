namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationValidationException : Exception
{
    public EvidenceEvaluationValidationException(string message)
        : base(message)
    {
    }
}

using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationValidationException : Exception, IValidationFailure
{
    public EvidenceEvaluationValidationException(string message)
        : base(message)
    {
        Issues = [new ValidationIssue(
            ValidationIssueCodes.EvaluationContractViolation,
            null,
            "Evaluation semantic repair is not enabled for this stage; preserve explicit Unknown or InsufficientSource states and fail closed.",
            ValidationIssueDisposition.NonRepairable)];
    }

    public IReadOnlyCollection<ValidationIssue> Issues { get; }
}

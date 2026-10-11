using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationValidationException : Exception, IValidationFailure
{
    public EvidenceEvaluationValidationException(string message, ValidationIssue? issue = null)
        : base(message)
    {
        Issues = [issue ?? new ValidationIssue(
            ValidationIssueCodes.EvaluationContractViolation,
            null,
            "This evaluation contract violation is not eligible for repair; preserve explicit Unknown or InsufficientSource states and fail closed.",
            ValidationIssueDisposition.NonRepairable)];
    }

    public IReadOnlyCollection<ValidationIssue> Issues { get; }
}

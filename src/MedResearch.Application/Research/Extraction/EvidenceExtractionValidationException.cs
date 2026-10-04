using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractionValidationException : Exception, IValidationFailure
{
    public EvidenceExtractionValidationException(string message)
        : this(message, new ValidationIssue(
            ValidationIssueCodes.ExtractionContractViolation,
            null,
            "Return a complete replacement that satisfies every extraction field constraint and preserves unavailable scientific values as null.",
            ValidationIssueDisposition.Repairable))
    {
    }

    public EvidenceExtractionValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues = [new ValidationIssue(
            ValidationIssueCodes.ExtractionContractViolation,
            null,
            "Return a complete replacement that satisfies every extraction field constraint and preserves unavailable scientific values as null.",
            ValidationIssueDisposition.Repairable)];
    }

    public EvidenceExtractionValidationException(string message, ValidationIssue issue)
        : base(message)
    {
        Issues = [issue];
    }

    public IReadOnlyCollection<ValidationIssue> Issues { get; }
}

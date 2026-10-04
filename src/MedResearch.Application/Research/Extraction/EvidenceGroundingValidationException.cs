using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceGroundingValidationException : Exception, IValidationFailure
{
    public EvidenceGroundingValidationException(string message)
        : this(message, new ValidationIssue(
            ValidationIssueCodes.SupportingTextNotGrounded,
            "findings[].supportingText",
            "Use a short verbatim excerpt that occurs in the supplied SourceMaterial, or omit the finding when the fact is not available.",
            ValidationIssueDisposition.Repairable))
    {
    }

    public EvidenceGroundingValidationException(string message, ValidationIssue issue)
        : base(message)
    {
        Issues = [issue];
    }

    public IReadOnlyCollection<ValidationIssue> Issues { get; }
}

using MedResearch.Application.Research.Validation;

namespace MedResearch.Application.Research.Synthesis;

public sealed class ResearchSynthesisValidationException : Exception, IValidationFailure
{
    public ResearchSynthesisValidationException(string message)
        : this(message, new ValidationIssue(
            ValidationIssueCodes.SynthesisContractViolation,
            null,
            "Return a complete replacement report that satisfies the supplied synthesis context and cites only supplied EvidenceId values.",
            ValidationIssueDisposition.Repairable))
    {
    }

    public ResearchSynthesisValidationException(string message, ValidationIssue issue)
        : base(message)
    {
        Issues = [issue];
    }

    public IReadOnlyCollection<ValidationIssue> Issues { get; }
}

namespace MedResearch.Application.Research.Validation;

public enum ValidationIssueDisposition
{
    Repairable,
    NonRepairable
}

public sealed record ValidationIssue(
    string Code,
    string? Path,
    string RepairInstruction,
    ValidationIssueDisposition Disposition);

public interface IValidationFailure
{
    IReadOnlyCollection<ValidationIssue> Issues { get; }
}

public static class ValidationIssueCodes
{
    public const string SupportingTextNotGrounded = "SupportingTextNotGrounded";
    public const string ExtractionContractViolation = "ExtractionContractViolation";
    public const string EvaluationContractViolation = "EvaluationContractViolation";
    public const string MixedClaimConflict = "MixedClaimConflict";
    public const string UnknownEvidenceReference = "UnknownEvidenceReference";
    public const string CrossRunEvidenceReference = "CrossRunEvidenceReference";
    public const string InvalidDirection = "InvalidDirection";
    public const string ModelSuppliedCitationMetadata = "ModelSuppliedCitationMetadata";
    public const string SynthesisContractViolation = "SynthesisContractViolation";
    public const string EvidenceCorpusInvariant = "EvidenceCorpusInvariant";
}

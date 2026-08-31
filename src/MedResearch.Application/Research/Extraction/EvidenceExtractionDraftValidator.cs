using MedResearch.Domain;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractionDraftValidator
{
    private const int MaxFindings = 12;
    private static readonly HashSet<string> SupportedStudyDesigns = new(StringComparer.OrdinalIgnoreCase)
    {
        "randomized controlled trial",
        "controlled trial",
        "cohort study",
        "case-control study",
        "cross-sectional study",
        "systematic review",
        "meta-analysis",
        "observational study",
        "experimental study",
        "qualitative study",
        "review",
        "case report",
        "other"
    };

    private readonly EvidenceGroundingValidator _groundingValidator;
    private readonly EvidenceNumericGroundingValidator _numericGroundingValidator;

    public EvidenceExtractionDraftValidator()
        : this(new EvidenceGroundingValidator(), new EvidenceNumericGroundingValidator())
    {
    }

    public EvidenceExtractionDraftValidator(
        EvidenceGroundingValidator groundingValidator,
        EvidenceNumericGroundingValidator numericGroundingValidator)
    {
        _groundingValidator = groundingValidator;
        _numericGroundingValidator = numericGroundingValidator;
    }

    public IReadOnlyCollection<AcceptedEvidenceFinding> Validate(
        EvidenceExtractionStudyContext context,
        EvidenceExtractionDraft draft)
    {
        if (string.IsNullOrWhiteSpace(context.Abstract))
        {
            throw new EvidenceExtractionValidationException("Cannot validate evidence extraction without source text.");
        }

        var findings = draft.Findings?.ToArray() ?? [];
        if (findings.Length > MaxFindings)
        {
            throw new EvidenceExtractionValidationException($"Evidence extraction returned more than {MaxFindings} findings.");
        }

        var accepted = new List<AcceptedEvidenceFinding>(findings.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            var outcome = NormalizeRequired(finding.Outcome, "Evidence outcome is required.", 300);
            var resultSummary = NormalizeRequired(finding.ResultSummary, "Evidence result summary is required.", 800);
            var supportingText = NormalizeRequired(finding.SupportingText, "Evidence supporting text is required.", 1_000);

            if (!_groundingValidator.TryValidate(context.Abstract, supportingText, out var groundingError))
            {
                throw new EvidenceGroundingValidationException(groundingError);
            }

            var dedupeKey = string.Join('|',
                EvidenceGroundingValidator.NormalizeForContainment(outcome),
                EvidenceGroundingValidator.NormalizeForContainment(resultSummary),
                EvidenceGroundingValidator.NormalizeForContainment(supportingText));
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            var direction = ParseDirection(finding.Direction);
            var studyDesign = NormalizeOptional(finding.StudyDesign, 100);
            if (studyDesign is not null && !SupportedStudyDesigns.Contains(studyDesign))
            {
                throw new EvidenceExtractionValidationException($"Unsupported study design '{studyDesign}'.");
            }

            accepted.Add(new AcceptedEvidenceFinding(
                outcome,
                resultSummary,
                supportingText,
                direction,
                NormalizeOptional(finding.Population, 300),
                NormalizeOptional(finding.ExposureOrIntervention, 300),
                NormalizeOptional(finding.Comparator, 300),
                studyDesign,
                KeepGroundedInt(context.Abstract, finding.SampleSize),
                NormalizeOptional(finding.EffectMeasure, 100),
                KeepGroundedDecimal(context.Abstract, finding.EffectValue),
                KeepGroundedDecimal(context.Abstract, finding.ConfidenceIntervalLower),
                KeepGroundedDecimal(context.Abstract, finding.ConfidenceIntervalUpper),
                KeepGroundedDecimal(context.Abstract, finding.PValue)));
        }

        return accepted;
    }

    private static EvidenceDirection ParseDirection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EvidenceDirection.NotReported;
        }

        if (Enum.TryParse<EvidenceDirection>(value.Trim(), ignoreCase: true, out var direction))
        {
            return direction;
        }

        throw new EvidenceExtractionValidationException($"Unsupported evidence direction '{value}'.");
    }

    private int? KeepGroundedInt(string sourceText, int? value)
    {
        return value.HasValue && _numericGroundingValidator.IsGrounded(sourceText, value.Value)
            ? value
            : null;
    }

    private decimal? KeepGroundedDecimal(string sourceText, decimal? value)
    {
        return value.HasValue && _numericGroundingValidator.IsGrounded(sourceText, value.Value)
            ? value
            : null;
    }

    private static string NormalizeRequired(string? value, string message, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        return normalized ?? throw new EvidenceExtractionValidationException(message);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maxLength)
        {
            throw new EvidenceExtractionValidationException($"Evidence extraction field exceeds {maxLength} characters.");
        }

        return normalized;
    }
}

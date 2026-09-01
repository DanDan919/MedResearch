using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationDraftValidator
{
    private const int MaxRationaleLength = 1_000;
    private const int MaxLimitationLength = 300;
    private const int MaxReportingLimitations = 12;
    private const int MaxAuthorReportedLimitations = 8;

    public EvidenceEvaluationResult Validate(
        EvaluationStudyContext context,
        EvidenceEvaluationDraft draft,
        EvidenceEvaluationSignalSet signals,
        string provider,
        string model,
        DateTimeOffset evaluatedAt)
    {
        if (draft.QualityScore.HasValue)
        {
            throw new EvidenceEvaluationValidationException("Numeric quality scores are not accepted by the evidence evaluation contract.");
        }

        ValidateIdentity(context.ResearchRunId, draft.ResearchRunId, "researchRunId");
        ValidateIdentity(context.StudyId, draft.StudyId, "studyId");

        foreach (var evidence in context.Evidence)
        {
            if (evidence.GroundingValidated is false)
            {
                throw new EvidenceEvaluationValidationException("Evidence evaluation requires grounded evidence findings.");
            }
        }

        var studyDesign = ParseEnum<StudyDesignClassification>(draft.StudyDesign, nameof(draft.StudyDesign));
        if (studyDesign == StudyDesignClassification.Unknown && signals.MetadataStudyDesignHint != StudyDesignClassification.Unknown)
        {
            studyDesign = signals.MetadataStudyDesignHint;
        }

        var sampleInformation = ParseEnum<MethodologicalAssessmentState>(draft.SampleInformation, nameof(draft.SampleInformation));
        var comparatorPresence = ParseEnum<ComparatorPresence>(draft.ComparatorPresence, nameof(draft.ComparatorPresence));
        var randomization = ParseEnum<MethodologicalAssessmentState>(draft.Randomization, nameof(draft.Randomization));
        var blinding = ParseEnum<MethodologicalAssessmentState>(draft.Blinding, nameof(draft.Blinding));
        var allocationConcealment = ParseEnum<MethodologicalAssessmentState>(draft.AllocationConcealment, nameof(draft.AllocationConcealment));
        var attritionMissingData = ParseEnum<MethodologicalAssessmentState>(draft.AttritionMissingData, nameof(draft.AttritionMissingData));
        var precision = ParseEnum<MethodologicalAssessmentState>(draft.Precision, nameof(draft.Precision));
        var directness = ParseEnum<DirectnessRating>(draft.Directness, nameof(draft.Directness));
        var overallConfidence = ParseEnum<MethodologicalConfidence>(draft.OverallConfidence, nameof(draft.OverallConfidence));
        var rationale = NormalizeRequired(draft.Rationale, "Evidence evaluation rationale is required.", MaxRationaleLength);
        var comparatorDescription = NormalizeOptional(draft.ComparatorDescription, 300);
        var reportingLimitations = MergeLimitations(signals.ReportingLimitations, draft.ReportingLimitations, MaxReportingLimitations);
        var authorReportedLimitations = NormalizeLimitations(draft.AuthorReportedLimitations, MaxAuthorReportedLimitations);

        ValidateAuthorReportedLimitations(context, authorReportedLimitations);
        ValidateConcernHasRationale(sampleInformation, rationale, nameof(sampleInformation));
        ValidateConcernHasRationale(randomization, rationale, nameof(randomization));
        ValidateConcernHasRationale(blinding, rationale, nameof(blinding));
        ValidateConcernHasRationale(allocationConcealment, rationale, nameof(allocationConcealment));
        ValidateConcernHasRationale(attritionMissingData, rationale, nameof(attritionMissingData));
        ValidateConcernHasRationale(precision, rationale, nameof(precision));
        RejectAbsenceAsConcern(sampleInformation, rationale, nameof(sampleInformation));
        RejectAbsenceAsConcern(randomization, rationale, nameof(randomization));
        RejectAbsenceAsConcern(blinding, rationale, nameof(blinding));
        RejectAbsenceAsConcern(allocationConcealment, rationale, nameof(allocationConcealment));
        RejectAbsenceAsConcern(attritionMissingData, rationale, nameof(attritionMissingData));
        RejectAbsenceAsConcern(precision, rationale, nameof(precision));

        if (context.SourceScope == EvidenceSourceScope.Abstract)
        {
            ApplyAbstractSourceRules(
                context,
                studyDesign,
                ref randomization,
                ref blinding,
                ref allocationConcealment,
                ref attritionMissingData);
        }

        if (signals.HasComparator && comparatorPresence is ComparatorPresence.Absent or ComparatorPresence.Unclear)
        {
            comparatorPresence = ComparatorPresence.Present;
            comparatorDescription ??= context.Evidence.Select(item => item.Comparator).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? context.Plan?.Comparator;
        }

        if (!signals.HasSampleSize && sampleInformation == MethodologicalAssessmentState.Favorable)
        {
            sampleInformation = MethodologicalAssessmentState.Unknown;
        }

        if (!signals.HasConfidenceInterval && !signals.HasEffectEstimate && !signals.HasSampleSize && precision == MethodologicalAssessmentState.Favorable)
        {
            precision = MethodologicalAssessmentState.Unknown;
        }

        overallConfidence = FinalizeOverallConfidence(
            overallConfidence,
            context.SourceScope,
            signals,
            sampleInformation,
            randomization,
            blinding,
            allocationConcealment,
            attritionMissingData,
            precision);

        var states = new[]
        {
            sampleInformation,
            randomization,
            blinding,
            allocationConcealment,
            attritionMissingData,
            precision
        };
        var unknownDomainCount = states.Count(state => state == MethodologicalAssessmentState.Unknown)
            + (comparatorPresence == ComparatorPresence.Unclear ? 1 : 0)
            + (directness == DirectnessRating.Unclear ? 1 : 0)
            + (studyDesign == StudyDesignClassification.Unknown ? 1 : 0);
        var insufficientSourceDomainCount = states.Count(state => state == MethodologicalAssessmentState.InsufficientSource)
            + (comparatorPresence == ComparatorPresence.InsufficientSource ? 1 : 0);

        return new EvidenceEvaluationResult(
            context.ResearchRunId,
            context.StudyId,
            context.Evidence.Select(evidence => evidence.EvidenceId).ToArray(),
            EvidenceEvaluationStatus.Completed,
            null,
            context.SourceScope,
            provider,
            model,
            EvidenceEvaluationPrompt.Version,
            evaluatedAt,
            studyDesign,
            sampleInformation,
            comparatorPresence,
            comparatorDescription,
            randomization,
            blinding,
            allocationConcealment,
            attritionMissingData,
            precision,
            directness,
            overallConfidence,
            rationale,
            reportingLimitations,
            authorReportedLimitations,
            signals,
            unknownDomainCount,
            insufficientSourceDomainCount);
    }

    private static void ValidateIdentity(Guid expected, string? actual, string propertyName)
    {
        if (!Guid.TryParse(actual, out var parsed) || parsed != expected)
        {
            throw new EvidenceEvaluationValidationException($"Evaluation draft did not preserve authoritative {propertyName}.");
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, string propertyName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: false, out var parsed))
        {
            throw new EvidenceEvaluationValidationException($"Unsupported or missing evaluation category for {propertyName}.");
        }

        return parsed;
    }

    private static void ValidateConcernHasRationale(MethodologicalAssessmentState state, string rationale, string domain)
    {
        if (state is MethodologicalAssessmentState.SomeConcern or MethodologicalAssessmentState.SeriousConcern
            && string.IsNullOrWhiteSpace(rationale))
        {
            throw new EvidenceEvaluationValidationException($"Concern category for {domain} requires a rationale.");
        }
    }

    private static void RejectAbsenceAsConcern(MethodologicalAssessmentState state, string rationale, string domain)
    {
        if (state is not (MethodologicalAssessmentState.SomeConcern or MethodologicalAssessmentState.SeriousConcern))
        {
            return;
        }

        var normalized = EvidenceGroundingValidator.NormalizeForContainment(rationale);
        if (normalized.Contains("not reported", StringComparison.Ordinal)
            || normalized.Contains("not available", StringComparison.Ordinal)
            || normalized.Contains("insufficient source", StringComparison.Ordinal)
            || normalized.Contains("abstract lacks", StringComparison.Ordinal)
            || normalized.Contains("not described", StringComparison.Ordinal))
        {
            throw new EvidenceEvaluationValidationException($"Absence of source detail cannot be converted into a concern for {domain}.");
        }
    }

    private static void ApplyAbstractSourceRules(
        EvaluationStudyContext context,
        StudyDesignClassification studyDesign,
        ref MethodologicalAssessmentState randomization,
        ref MethodologicalAssessmentState blinding,
        ref MethodologicalAssessmentState allocationConcealment,
        ref MethodologicalAssessmentState attritionMissingData)
    {
        var source = BuildSourceText(context);
        var isInterventional = studyDesign == StudyDesignClassification.RandomizedControlledTrial;

        if (!isInterventional)
        {
            randomization = MethodologicalAssessmentState.NotApplicable;
            allocationConcealment = MethodologicalAssessmentState.NotApplicable;
        }
        else if (!ContainsAny(source, "randomized", "randomised", "randomly"))
        {
            randomization = MethodologicalAssessmentState.InsufficientSource;
        }

        if (!ContainsAny(source, "blind", "masked", "masking"))
        {
            blinding = MethodologicalAssessmentState.InsufficientSource;
        }

        if (allocationConcealment != MethodologicalAssessmentState.NotApplicable
            && !ContainsAny(source, "allocation conceal", "concealed allocation"))
        {
            allocationConcealment = MethodologicalAssessmentState.InsufficientSource;
        }

        if (!ContainsAny(source, "attrition", "dropout", "drop-out", "withdraw", "lost to follow", "missing data"))
        {
            attritionMissingData = MethodologicalAssessmentState.InsufficientSource;
        }
    }

    private static MethodologicalConfidence FinalizeOverallConfidence(
        MethodologicalConfidence requested,
        EvidenceSourceScope sourceScope,
        EvidenceEvaluationSignalSet signals,
        params MethodologicalAssessmentState[] states)
    {
        var insufficientSourceCount = states.Count(state => state == MethodologicalAssessmentState.InsufficientSource);
        var seriousConcernCount = states.Count(state => state == MethodologicalAssessmentState.SeriousConcern);

        if (sourceScope == EvidenceSourceScope.Abstract && insufficientSourceCount >= 3)
        {
            return MethodologicalConfidence.InsufficientInformation;
        }

        if (seriousConcernCount > 0 && requested == MethodologicalConfidence.Higher)
        {
            return MethodologicalConfidence.Moderate;
        }

        if (requested == MethodologicalConfidence.Higher
            && signals.HasPValue
            && !signals.HasConfidenceInterval
            && !signals.HasEffectEstimate
            && !signals.HasSampleSize)
        {
            return MethodologicalConfidence.Moderate;
        }

        return requested;
    }

    private static void ValidateAuthorReportedLimitations(
        EvaluationStudyContext context,
        IReadOnlyCollection<string> limitations)
    {
        if (limitations.Count == 0)
        {
            return;
        }

        var sourceText = BuildSourceText(context);
        foreach (var limitation in limitations)
        {
            if (!Contains(sourceText, limitation))
            {
                throw new EvidenceEvaluationValidationException("Author-reported limitations must be grounded in supplied source text or evidence excerpts.");
            }
        }
    }

    private static string BuildSourceText(EvaluationStudyContext context)
    {
        return string.Join(' ', new[] { context.Abstract ?? string.Empty }.Concat(context.Evidence.Select(evidence => evidence.SupportingText)));
    }

    private static bool ContainsAny(string sourceText, params string[] needles)
    {
        var normalized = EvidenceGroundingValidator.NormalizeForContainment(sourceText);
        return needles.Any(needle => normalized.Contains(EvidenceGroundingValidator.NormalizeForContainment(needle), StringComparison.Ordinal));
    }

    private static bool Contains(string sourceText, string needle)
    {
        return EvidenceGroundingValidator.NormalizeForContainment(sourceText)
            .Contains(EvidenceGroundingValidator.NormalizeForContainment(needle), StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> MergeLimitations(
        IReadOnlyCollection<string> deterministic,
        IReadOnlyCollection<string>? draft,
        int maxItems)
    {
        return deterministic
            .Concat(NormalizeLimitations(draft, maxItems))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToArray();
    }

    private static IReadOnlyCollection<string> NormalizeLimitations(IReadOnlyCollection<string>? values, int maxItems)
    {
        if (values is null)
        {
            return [];
        }

        var normalized = values
            .Select(value => NormalizeOptional(value, MaxLimitationLength))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length > maxItems)
        {
            throw new EvidenceEvaluationValidationException($"Evaluation limitation collection exceeds {maxItems} items.");
        }

        return normalized;
    }

    private static string NormalizeRequired(string? value, string message, int maxLength)
    {
        return NormalizeOptional(value, maxLength) ?? throw new EvidenceEvaluationValidationException(message);
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
            throw new EvidenceEvaluationValidationException($"Evaluation text exceeds {maxLength} characters.");
        }

        return normalized;
    }
}

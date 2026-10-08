using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Validation;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public static class StructuredResearchClaimValidator
{
    public const string ProtocolVersion = "structured-claim-v1";

    public static ResearchClaimSemantics Validate(SynthesisContext context, ResearchReportClaimDraft draft,
        ResearchReportClaimDirection direction, Guid[] ids, ResearchReportStatus status, int index)
    {
        var path = $"claims[{index}]";
        if (!string.IsNullOrWhiteSpace(draft.Text) || draft.UnrecognizedFields?.Count > 0)
            Reject(ValidationIssueCodes.UnsupportedNumericAssertion, path, "Do not propose free claim text or numeric values. Propose structured scope and authoritative references only.");
        var kind = Parse<ResearchClaimKind>(draft.Kind, path + ".kind");
        var evidence = context.Studies.SelectMany(x => x.Evidence).Where(x => ids.Contains(x.EvidenceId)).ToArray();
        if (evidence.Length != ids.Length) Reject(ValidationIssueCodes.UnknownEvidenceReference, path + ".evidenceIds", "Cite only current context Evidence IDs.");
        if (status == ResearchReportStatus.InsufficientEvidence && kind != ResearchClaimKind.InsufficientEvidence)
            Reject(ValidationIssueCodes.InsufficientEvidenceOverclaim, path + ".kind", "An insufficient report cannot assert an effect; use InsufficientEvidence or no claims.");
        if (kind == ResearchClaimKind.InsufficientEvidence)
        {
            if (status != ResearchReportStatus.InsufficientEvidence || direction != ResearchReportClaimDirection.NotApplicable || ids.Length != 0 ||
                new[] { draft.Outcome, draft.Population, draft.ExposureOrIntervention, draft.Comparator, draft.Timepoint, draft.NumericEvidenceId, draft.QuantitativeArtifactId, draft.Statistic }.Any(x => x is not null))
                Reject(ValidationIssueCodes.InsufficientEvidenceOverclaim, path, "Insufficiency is a run-level metadata statement, not no effect. Use no support IDs, scope, numbers or direction.");
            return new(ProtocolVersion, kind, null, null, null, null, null, direction, [], null, null, null, null, null, null);
        }
        if (ids.Length == 0) Reject(ValidationIssueCodes.UnknownEvidenceReference, path + ".evidenceIds", "Scientific claims require supporting Evidence.");
        Match(draft.Outcome, evidence.Select(x => (string?)x.Outcome), ValidationIssueCodes.ClaimOutcomeMismatch, path + ".outcome");
        Match(draft.Population, evidence.Select(x => x.Population), ValidationIssueCodes.ClaimPopulationMismatch, path + ".population");
        Match(draft.ExposureOrIntervention, evidence.Select(x => x.ExposureOrIntervention), ValidationIssueCodes.ClaimInterventionMismatch, path + ".exposureOrIntervention");
        Match(draft.Comparator, evidence.Select(x => x.Comparator), ValidationIssueCodes.ClaimComparatorMismatch, path + ".comparator");
        Match(draft.Timepoint, evidence.Select(x => x.Timepoint), ValidationIssueCodes.ClaimTimepointMismatch, path + ".timepoint");

        var directions = evidence.Select(x => x.Direction).Distinct().ToArray();
        if (kind == ResearchClaimKind.MixedEvidence)
        {
            if (direction != ResearchReportClaimDirection.Mixed || !(directions.Length > 1 || directions.Contains(EvidenceDirection.Mixed)))
                Reject(ValidationIssueCodes.MixedEvidenceOverstated, path + ".direction", "Mixed claims require differing cited directions or a reported Mixed direction.");
        }
        else if (kind == ResearchClaimKind.QualitativeEffect)
        {
            var expected = direction switch
            {
                ResearchReportClaimDirection.Positive => EvidenceDirection.Positive,
                ResearchReportClaimDirection.Negative => EvidenceDirection.Negative,
                ResearchReportClaimDirection.NoClearEffect => EvidenceDirection.NoClearEffect,
                ResearchReportClaimDirection.NotReported => EvidenceDirection.NotReported,
                _ => (EvidenceDirection?)null
            };
            if (expected is null || directions.Any(x => x != expected))
                Reject(directions.Length > 1 ? ValidationIssueCodes.MixedEvidenceOverstated : ValidationIssueCodes.InvalidDirection, path + ".direction", "Every cited finding must support the proposed direction; use MixedEvidence for differing directions.");
        }
        else if (direction != ResearchReportClaimDirection.NotApplicable)
            Reject(ValidationIssueCodes.InvalidDirection, path + ".direction", "Numeric statements use NotApplicable; do not infer clinical benefit or causality from a numeric estimate.");

        ResearchClaimStatistic? statistic = null;
        ResearchClaimNumericSnapshot? numeric = null;
        Guid? numericEvidenceId = null, artifactId = null;
        string? groupKey = null, fingerprint = null;
        if (kind is ResearchClaimKind.ReportedStudyResult or ResearchClaimKind.QuantitativeSynthesis)
        {
            statistic = Parse<ResearchClaimStatistic>(draft.Statistic, path + ".statistic");
            if (kind == ResearchClaimKind.ReportedStudyResult)
            {
                if (!Guid.TryParse(draft.NumericEvidenceId, out var numericId) || ids.Length != 1 || ids[0] != numericId || draft.QuantitativeArtifactId is not null)
                    Reject(ValidationIssueCodes.UnsupportedNumericAssertion, path, "A reported numeric claim must reference exactly its single supporting NumericEvidenceId, not an artifact or another study.");
                numericEvidenceId = ids[0];
                numeric = StudyNumeric(evidence[0], statistic.Value, path);
            }
            else
            {
                if (draft.NumericEvidenceId is not null || !Guid.TryParse(draft.QuantitativeArtifactId, out var parsed))
                    Reject(ValidationIssueCodes.QuantitativeArtifactMismatch, path, "Use the exact persisted artifact ID, not a study numeric reference.");
                var artifact = (context.QuantitativeArtifacts ?? []).SingleOrDefault(x => x.ArtifactId.ToString() == draft.QuantitativeArtifactId);
                if (artifact is null || artifact.Result.ResearchRunId != context.ResearchRunId || artifact.Result.Status != QuantitativeSynthesisStatus.Synthesized)
                    Reject(ValidationIssueCodes.QuantitativeArtifactMismatch, path + ".quantitativeArtifactId", "Reference a synthesized current-run persisted artifact supplied in context.");
                if (!ids.Order().SequenceEqual(artifact!.Result.Contributions.Select(x => x.EvidenceId).Distinct().Order()))
                    Reject(ValidationIssueCodes.QuantitativeArtifactMismatch, path + ".evidenceIds", "Cite exactly the artifact's contribution Evidence IDs; no substitution or extra support.");
                artifactId = artifact.ArtifactId;
                groupKey = artifact.Result.GroupKey;
                fingerprint = artifact.SnapshotFingerprint;
                numeric = ArtifactNumeric(artifact.Result, statistic.Value, path);
            }
        }
        else if (draft.NumericEvidenceId is not null || draft.QuantitativeArtifactId is not null || draft.Statistic is not null)
            Reject(ValidationIssueCodes.UnsupportedNumericAssertion, path, "Qualitative/mixed claims cannot include numeric references; choose a numeric claim kind.");

        return new(ProtocolVersion, kind, Normalize(draft.Outcome), Normalize(draft.Population), Normalize(draft.ExposureOrIntervention),
            Normalize(draft.Comparator), Normalize(draft.Timepoint), direction, ids.Order().ToArray(), numericEvidenceId, artifactId, groupKey, fingerprint, statistic, numeric);
    }

    private static ResearchClaimNumericSnapshot StudyNumeric(SynthesisEvidenceContext evidence, ResearchClaimStatistic statistic, string path)
    {
        // This projection rechecks current tuple predicates; production context/store also authenticates exact source membership.
        var item = SynthesisEvidenceProjection.Create(evidence);
        var value = statistic switch
        {
            ResearchClaimStatistic.StudyEffect => item.EffectValue,
            ResearchClaimStatistic.StudyConfidenceInterval => item.EffectValue,
            ResearchClaimStatistic.StudyStandardError => item.ReportedStandardError,
            ResearchClaimStatistic.StudyPValue => item.PValue,
            ResearchClaimStatistic.StudySampleSize => item.SampleSize,
            _ => null
        };
        if (value is null || (statistic is ResearchClaimStatistic.StudyEffect or ResearchClaimStatistic.StudyConfidenceInterval && item.EffectMeasure is null) ||
            (statistic == ResearchClaimStatistic.StudyConfidenceInterval && (item.ConfidenceIntervalLower is null || item.ConfidenceIntervalUpper is null || item.ConfidenceLevel is null)))
            Reject(ValidationIssueCodes.UnsupportedNumericAssertion, path + ".statistic", "The requested statistic must have verified source-grounded fields; missing or rejected values cannot be asserted.");
        var includeCi = statistic == ResearchClaimStatistic.StudyConfidenceInterval;
        return new(statistic is ResearchClaimStatistic.StudyEffect or ResearchClaimStatistic.StudyConfidenceInterval ? item.EffectMeasure! : statistic.ToString(),
            value, null, includeCi ? item.ConfidenceIntervalLower : null, includeCi ? item.ConfidenceIntervalUpper : null, null, null,
            includeCi ? item.ConfidenceLevel : null, statistic == ResearchClaimStatistic.StudyPValue ? item.PValueOperator ?? "=" : "=", null, null);
    }

    private static ResearchClaimNumericSnapshot ArtifactNumeric(QuantitativeSynthesisResult result, ResearchClaimStatistic statistic, string path)
    {
        double? value = null, lower = null, upper = null;
        decimal? level = null;
        int? df = null;
        var algorithm = result.AlgorithmVersion;
        var label = result.EffectMeasureType.ToString();
        switch (statistic)
        {
            case ResearchClaimStatistic.FixedEffectWald:
                value = result.ReportedScaleEffect; lower = result.ReportedScaleConfidenceIntervalLower; upper = result.ReportedScaleConfidenceIntervalUpper; level = result.OutputConfidenceLevel;
                break;
            case ResearchClaimStatistic.RandomEffectsWald when result.RandomEffects?.Status == QuantitativeSynthesisStatus.Synthesized:
                var random = result.RandomEffects;
                value = random.ReportedScaleEffect; lower = random.ReportedScaleConfidenceIntervalLower; upper = random.ReportedScaleConfidenceIntervalUpper; level = random.OutputConfidenceLevel; algorithm = random.AlgorithmVersion;
                break;
            case ResearchClaimStatistic.RandomEffectsHksj when result.RandomEffects?.HksjInference?.Status == QuantitativeSynthesisStatus.Synthesized:
                var hksj = result.RandomEffects.HksjInference;
                value = hksj.ReportedScaleEffect; lower = hksj.ReportedScaleConfidenceIntervalLower; upper = hksj.ReportedScaleConfidenceIntervalUpper; level = hksj.OutputConfidenceLevel; df = hksj.DegreesOfFreedom; algorithm = hksj.AlgorithmVersion;
                break;
            case ResearchClaimStatistic.RandomEffectsPredictionInterval when result.RandomEffects?.PredictionInterval?.Status == QuantitativeSynthesisStatus.Synthesized:
                var pi = result.RandomEffects.PredictionInterval;
                value = pi.ReportedScaleEffect; lower = pi.ReportedScaleLower; upper = pi.ReportedScaleUpper; level = pi.OutputConfidenceLevel; df = pi.DegreesOfFreedom; algorithm = pi.AlgorithmVersion;
                break;
            case ResearchClaimStatistic.CochransQ:
                value = result.HeterogeneityDiagnostics?.CochransQ; df = result.HeterogeneityDiagnostics?.DegreesOfFreedom; label = "Cochran's Q"; algorithm = result.HeterogeneityDiagnostics?.AlgorithmVersion;
                break;
            case ResearchClaimStatistic.ISquared:
                value = result.HeterogeneityDiagnostics?.ISquared; label = "I-squared (proportion)"; algorithm = result.HeterogeneityDiagnostics?.AlgorithmVersion;
                break;
            case ResearchClaimStatistic.TauSquared when result.BetweenStudyVariance?.Status == BetweenStudyVarianceEstimateStatus.Estimated:
                value = result.BetweenStudyVariance.TauSquared; label = "REML tau-squared (analysis-scale variance)"; algorithm = result.BetweenStudyVariance.AlgorithmVersion;
                break;
        }
        if (value is null || !double.IsFinite(value.Value) || (lower is not null && !double.IsFinite(lower.Value)) || (upper is not null && !double.IsFinite(upper.Value)) ||
            (statistic is ResearchClaimStatistic.FixedEffectWald or ResearchClaimStatistic.RandomEffectsWald or ResearchClaimStatistic.RandomEffectsHksj or ResearchClaimStatistic.RandomEffectsPredictionInterval && (lower is null || upper is null || level is null)))
            Reject(ValidationIssueCodes.UnsupportedNumericAssertion, path + ".statistic", "The selected persisted statistic must be available and finite; do not substitute CI/PI or an unestimated value.");
        return new(label, null, value, null, null, lower, upper, level, "=", df, algorithm);
    }

    private static void Match(string? proposed, IEnumerable<string?> actual, string code, string path)
    {
        var value = Normalize(proposed);
        if ((proposed?.Length ?? 0) > 512 || actual.Any(x => !string.Equals(Normalize(x), value, StringComparison.OrdinalIgnoreCase)))
            Reject(code, path, "Copy the exact supplied context label for all cited Evidence; no paraphrase, broader population, substitute intervention, or guessed missing field.");
    }

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static T Parse<T>(string? value, string path) where T : struct, Enum
    {
        if (value is null || !Enum.TryParse<T>(value, false, out var parsed) || !Enum.IsDefined(parsed) || Enum.GetName(parsed) != value)
            Reject(ValidationIssueCodes.SynthesisContractViolation, path, "Use an exact supported named category, never a numeric or undefined enum value.");
        return Enum.Parse<T>(value!, false);
    }

    private static void Reject(string code, string path, string instruction) => throw new ResearchSynthesisValidationException(instruction,
        new ValidationIssue(code, path, instruction, ValidationIssueDisposition.Repairable));
}

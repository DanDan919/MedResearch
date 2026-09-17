using System.Globalization;
using System.Text;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Quantitative;

public sealed class QuantitativeEvidenceAssessor : IQuantitativeEvidenceAssessor
{
    public const string AlgorithmVersion = "quantitative-eligibility-v1";

    public QuantitativeEvidenceReadiness Assess(EvidenceCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var sourcesById = corpus.SourceMaterials.ToDictionary(source => source.SourceMaterialId);
        var extractionSources = corpus.Extractions
            .Where(extraction => extraction.SourceMaterialId.HasValue)
            .ToDictionary(extraction => extraction.ExtractionId, extraction => extraction.SourceMaterialId!.Value);

        var assessments = corpus.Evidence
            .OrderBy(evidence => evidence.StudyId)
            .ThenBy(evidence => evidence.EvidenceId)
            .Select(evidence => AssessEvidence(evidence, extractionSources, sourcesById))
            .ToArray();

        var groups = assessments
            .Where(assessment => assessment.Eligibility == QuantitativeEligibility.Eligible)
            .Where(assessment => assessment.PopulationCompatibilityKey is not null)
            .Where(assessment => assessment.ComparatorCompatibilityKey is not null)
            .Where(assessment => assessment.StudyDesignCompatibilityKey is not null)
            .GroupBy(BuildGroupKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.StudyId).ThenBy(item => item.EvidenceId).ToArray();
                var studyIds = ordered.Select(item => item.StudyId).Distinct().OrderBy(id => id).ToArray();
                var hasDependentEvidence = ordered.Length != studyIds.Length;
                var first = ordered[0];
                return new CompatibleEvidenceGroup(
                    group.Key,
                    first.OutcomeGroupKey,
                    first.PopulationCompatibilityKey!,
                    first.ComparatorCompatibilityKey!,
                    first.StudyDesignCompatibilityKey!,
                    first.EffectMeasureType,
                    ordered.Select(item => item.EvidenceId).ToArray(),
                    studyIds,
                    ordered.Length,
                    studyIds.Length,
                    hasDependentEvidence,
                    ordered.Length >= 2 && !hasDependentEvidence);
            })
            .ToArray();

        return new QuantitativeEvidenceReadiness(
            corpus.ResearchRunId,
            assessments,
            groups,
            assessments.Count(assessment => assessment.Eligibility == QuantitativeEligibility.Eligible),
            assessments.Count(assessment => assessment.Eligibility == QuantitativeEligibility.Ineligible),
            AlgorithmVersion);
    }

    private static QuantitativeEvidenceAssessment AssessEvidence(
        SynthesisEvidenceContext evidence,
        IReadOnlyDictionary<Guid, Guid> extractionSources,
        IReadOnlyDictionary<Guid, SynthesisSourceMaterialSnapshot> sourcesById)
    {
        var reasons = new List<QuantitativeIneligibilityReason>();
        var sourceMaterialId = extractionSources.TryGetValue(evidence.EvidenceExtractionId, out var linkedSourceMaterialId)
            ? linkedSourceMaterialId
            : Guid.Empty;
        var sourceWasTruncated = sourceMaterialId != Guid.Empty
            && sourcesById.TryGetValue(sourceMaterialId, out var source)
            && source.WasTruncated;

        var outcomeKey = NormalizeCompatibilityKey(evidence.Outcome);
        if (string.IsNullOrWhiteSpace(outcomeKey))
        {
            reasons.Add(QuantitativeIneligibilityReason.OutcomeNotCompatible);
        }

        var populationKey = NormalizeCompatibilityKey(evidence.Population);
        if (populationKey is null)
        {
            reasons.Add(QuantitativeIneligibilityReason.PopulationNotCompatible);
        }

        var comparatorKey = NormalizeCompatibilityKey(evidence.Comparator);
        if (comparatorKey is null)
        {
            reasons.Add(QuantitativeIneligibilityReason.ComparatorNotCompatible);
        }

        var designKey = NormalizeCompatibilityKey(evidence.StudyDesign);
        if (designKey is null)
        {
            reasons.Add(QuantitativeIneligibilityReason.StudyDesignNotCompatible);
        }

        var effectMeasureType = ClassifyEffectMeasure(evidence.EffectMeasure);
        if (string.IsNullOrWhiteSpace(evidence.EffectMeasure))
        {
            reasons.Add(QuantitativeIneligibilityReason.MissingEffectMeasure);
        }
        else if (effectMeasureType == EffectMeasureType.Unknown)
        {
            reasons.Add(QuantitativeIneligibilityReason.UnknownEffectMeasure);
        }
        else if (!IsSupportedForNormalization(effectMeasureType))
        {
            reasons.Add(QuantitativeIneligibilityReason.UnsupportedEffectMeasure);
        }

        if (!evidence.EffectValue.HasValue)
        {
            reasons.Add(QuantitativeIneligibilityReason.MissingEffectValue);
        }

        NormalizedStatistic? statistic = null;
        if (reasons.Count == 0 || reasons.All(reason => reason is QuantitativeIneligibilityReason.PopulationNotCompatible
                or QuantitativeIneligibilityReason.ComparatorNotCompatible
                or QuantitativeIneligibilityReason.StudyDesignNotCompatible))
        {
            statistic = TryNormalize(evidence, effectMeasureType, reasons);
        }

        var eligibility = reasons.Count == 0 && statistic is not null
            ? QuantitativeEligibility.Eligible
            : QuantitativeEligibility.Ineligible;

        return new QuantitativeEvidenceAssessment(
            evidence.EvidenceId,
            evidence.ResearchRunId,
            evidence.StudyId,
            evidence.EvidenceExtractionId,
            sourceMaterialId,
            outcomeKey ?? string.Empty,
            populationKey,
            comparatorKey,
            designKey,
            evidence.EffectMeasure,
            effectMeasureType,
            eligibility,
            evidence.EffectValue,
            statistic?.NormalizedEffect,
            statistic?.StandardError,
            statistic?.Variance,
            statistic?.NormalizedEffectOrigin,
            statistic?.StandardErrorOrigin,
            sourceWasTruncated,
            reasons.Distinct().OrderBy(reason => reason).ToArray());
    }

    public static EffectMeasureType ClassifyEffectMeasure(string? reportedMeasure)
    {
        var normalized = NormalizeCompatibilityKey(reportedMeasure);
        if (normalized is null)
        {
            return EffectMeasureType.Unknown;
        }

        return normalized switch
        {
            "or" or "odds ratio" => EffectMeasureType.OddsRatio,
            "rr" or "risk ratio" or "relative risk" => EffectMeasureType.RiskRatio,
            "hr" or "hazard ratio" => EffectMeasureType.HazardRatio,
            "risk difference" or "absolute risk difference" => EffectMeasureType.RiskDifference,
            "md" or "mean difference" => EffectMeasureType.MeanDifference,
            "smd" or "standardized mean difference" or "standardised mean difference" or "cohen d" or "hedges g" => EffectMeasureType.StandardizedMeanDifference,
            "r" or "correlation" or "pearson r" or "spearman r" => EffectMeasureType.Correlation,
            "regression coefficient" or "beta" or "coefficient" => EffectMeasureType.RegressionCoefficient,
            "proportion" or "prevalence" => EffectMeasureType.Proportion,
            _ => EffectMeasureType.Other
        };
    }

    private static bool IsSupportedForNormalization(EffectMeasureType type)
    {
        return type is EffectMeasureType.OddsRatio
            or EffectMeasureType.RiskRatio
            or EffectMeasureType.HazardRatio
            or EffectMeasureType.RiskDifference
            or EffectMeasureType.MeanDifference
            or EffectMeasureType.StandardizedMeanDifference
            or EffectMeasureType.Correlation;
    }

    private static NormalizedStatistic? TryNormalize(
        SynthesisEvidenceContext evidence,
        EffectMeasureType effectMeasureType,
        ICollection<QuantitativeIneligibilityReason> reasons)
    {
        if (!evidence.EffectValue.HasValue)
        {
            return null;
        }

        var effectValue = (double)evidence.EffectValue.Value;
        if (!double.IsFinite(effectValue))
        {
            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        if (effectMeasureType is EffectMeasureType.OddsRatio or EffectMeasureType.RiskRatio or EffectMeasureType.HazardRatio)
        {
            if (effectValue <= 0)
            {
                reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
                return null;
            }

            var uncertainty = TryResolveUncertainty(evidence, requiresPositiveBounds: true, reasons);
            if (uncertainty is null)
            {
                return null;
            }

            return new NormalizedStatistic(
                Math.Log(effectValue),
                uncertainty.StandardError,
                uncertainty.StandardError * uncertainty.StandardError,
                StatisticOrigin.DerivedFromStandardError,
                uncertainty.Origin);
        }

        if (effectMeasureType is EffectMeasureType.RiskDifference or EffectMeasureType.MeanDifference or EffectMeasureType.StandardizedMeanDifference)
        {
            var uncertainty = TryResolveUncertainty(evidence, requiresPositiveBounds: false, reasons);
            if (uncertainty is null)
            {
                return null;
            }

            return new NormalizedStatistic(
                effectValue,
                uncertainty.StandardError,
                uncertainty.StandardError * uncertainty.StandardError,
                StatisticOrigin.Reported,
                uncertainty.Origin);
        }

        if (effectMeasureType == EffectMeasureType.Correlation)
        {
            if (effectValue <= -1 || effectValue >= 1)
            {
                reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
                return null;
            }

            if (evidence.SampleSize is not > 3)
            {
                reasons.Add(QuantitativeIneligibilityReason.MissingSampleSize);
                return null;
            }

            var fisherZ = 0.5d * Math.Log((1 + effectValue) / (1 - effectValue));
            var standardError = 1d / Math.Sqrt(evidence.SampleSize.Value - 3d);
            return new NormalizedStatistic(
                fisherZ,
                standardError,
                standardError * standardError,
                StatisticOrigin.DerivedFromSampleSize,
                StatisticOrigin.DerivedFromSampleSize);
        }

        reasons.Add(QuantitativeIneligibilityReason.UnsupportedEffectMeasure);
        return null;
    }

    private static ResolvedUncertainty? TryResolveUncertainty(
        SynthesisEvidenceContext evidence,
        bool requiresPositiveBounds,
        ICollection<QuantitativeIneligibilityReason> reasons)
    {
        if (evidence.ReportedStandardError.HasValue)
        {
            var reportedSe = (double)evidence.ReportedStandardError.Value;
            if (reportedSe > 0 && double.IsFinite(reportedSe))
            {
                return new ResolvedUncertainty(reportedSe, StatisticOrigin.Reported);
            }

            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        var hasAnyCi = evidence.ConfidenceIntervalLower.HasValue || evidence.ConfidenceIntervalUpper.HasValue;
        if (!hasAnyCi)
        {
            reasons.Add(QuantitativeIneligibilityReason.MissingUncertainty);
            return null;
        }

        if (!evidence.ConfidenceIntervalLower.HasValue || !evidence.ConfidenceIntervalUpper.HasValue)
        {
            reasons.Add(QuantitativeIneligibilityReason.MissingUncertainty);
            return null;
        }

        if (!evidence.ConfidenceLevel.HasValue)
        {
            reasons.Add(QuantitativeIneligibilityReason.MissingConfidenceLevel);
            return null;
        }

        var lower = (double)evidence.ConfidenceIntervalLower.Value;
        var upper = (double)evidence.ConfidenceIntervalUpper.Value;
        var confidenceLevel = (double)evidence.ConfidenceLevel.Value;
        if (!double.IsFinite(lower) || !double.IsFinite(upper) || !double.IsFinite(confidenceLevel)
            || lower > upper || confidenceLevel <= 0 || confidenceLevel >= 1)
        {
            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        if (requiresPositiveBounds && (lower <= 0 || upper <= 0))
        {
            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        var z = StandardNormalQuantile.Inverse(0.5d + confidenceLevel / 2d);
        if (!double.IsFinite(z) || z <= 0)
        {
            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        var standardError = requiresPositiveBounds
            ? (Math.Log(upper) - Math.Log(lower)) / (2d * z)
            : (upper - lower) / (2d * z);
        if (!double.IsFinite(standardError) || standardError <= 0)
        {
            reasons.Add(QuantitativeIneligibilityReason.InvalidNumericValue);
            return null;
        }

        return new ResolvedUncertainty(standardError, StatisticOrigin.DerivedFromConfidenceInterval);
    }

    private static string BuildGroupKey(QuantitativeEvidenceAssessment assessment)
    {
        return string.Join('|',
            assessment.OutcomeGroupKey,
            assessment.PopulationCompatibilityKey,
            assessment.ComparatorCompatibilityKey,
            assessment.StudyDesignCompatibilityKey,
            assessment.EffectMeasureType.ToString());
    }

    private static string? NormalizeCompatibilityKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        normalized = string.Join(' ', normalized.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed record NormalizedStatistic(
        double NormalizedEffect,
        double StandardError,
        double Variance,
        StatisticOrigin NormalizedEffectOrigin,
        StatisticOrigin StandardErrorOrigin);

    private sealed record ResolvedUncertainty(double StandardError, StatisticOrigin Origin);
}
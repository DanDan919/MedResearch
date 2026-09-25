namespace MedResearch.Application.Research.Quantitative;

public sealed class RandomEffectsQuantitativeStatisticalSynthesizer
{
    public const string AlgorithmVersion = "inverse-variance-random-effects-reml-wald-v1";

    private readonly QuantitativeSynthesisOptions _options;

    public RandomEffectsQuantitativeStatisticalSynthesizer(QuantitativeSynthesisOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public QuantitativeRandomEffectsSynthesisResult Synthesize(
        BetweenStudyVarianceEstimate betweenStudyVariance,
        IReadOnlyCollection<QuantitativeSynthesisContribution> commonEffectContributions,
        EffectMeasureType effectMeasureType)
    {
        ArgumentNullException.ThrowIfNull(betweenStudyVariance);
        ArgumentNullException.ThrowIfNull(commonEffectContributions);

        if (betweenStudyVariance.Status != BetweenStudyVarianceEstimateStatus.Estimated
            || !betweenStudyVariance.TauSquared.HasValue)
        {
            return CreateRejected(
                betweenStudyVariance,
                [],
                [QuantitativeRandomEffectsFailureReason.BetweenStudyVarianceNotEstimated]);
        }

        var tauSquared = betweenStudyVariance.TauSquared.Value;
        if (!double.IsFinite(tauSquared) || tauSquared < 0d)
        {
            return CreateRejected(
                betweenStudyVariance,
                [],
                [QuantitativeRandomEffectsFailureReason.InvalidTauSquared]);
        }

        var reasons = new List<QuantitativeRandomEffectsFailureReason>();
        var preflight = BuildPreflightContributions(commonEffectContributions, tauSquared, reasons);
        if (reasons.Count > 0)
        {
            return CreateRejected(betweenStudyVariance, preflight, reasons);
        }

        var totalWeight = preflight.Sum(contribution => contribution.Weight);
        if (!double.IsFinite(totalWeight) || totalWeight <= 0d)
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidWeight);
            return CreateRejected(betweenStudyVariance, preflight, reasons);
        }

        var contributions = preflight
            .Select(contribution => contribution with { NormalizedWeight = contribution.Weight / totalWeight })
            .ToArray();
        if (contributions.Any(contribution => !double.IsFinite(contribution.NormalizedWeight) || contribution.NormalizedWeight <= 0d))
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidWeight);
            return CreateRejected(betweenStudyVariance, contributions, reasons);
        }

        var normalizedWeightSum = contributions.Sum(contribution => contribution.NormalizedWeight);
        if (!double.IsFinite(normalizedWeightSum) || normalizedWeightSum <= 0d)
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidWeight);
            return CreateRejected(betweenStudyVariance, contributions, reasons);
        }

        var pooledEffect = contributions.Sum(contribution => contribution.Weight * contribution.AnalysisScaleEffect) / totalWeight;
        var pooledVariance = 1d / totalWeight;
        var pooledStandardError = Math.Sqrt(pooledVariance);
        if (!double.IsFinite(pooledEffect)
            || !double.IsFinite(pooledVariance)
            || !double.IsFinite(pooledStandardError)
            || pooledVariance <= 0d
            || pooledStandardError <= 0d)
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.NonFinitePooledEffect);
            return CreateRejected(betweenStudyVariance, contributions, reasons);
        }

        var z = StandardNormalQuantile.Inverse(0.5d + _options.BoundedOutputConfidenceLevel / 2d);
        var lower = pooledEffect - z * pooledStandardError;
        var upper = pooledEffect + z * pooledStandardError;
        if (!double.IsFinite(z) || z <= 0d || !double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.NonFiniteConfidenceInterval);
            return CreateRejected(betweenStudyVariance, contributions, reasons);
        }

        var reportedEffect = BackTransform(effectMeasureType, pooledEffect);
        var reportedLower = BackTransform(effectMeasureType, lower);
        var reportedUpper = BackTransform(effectMeasureType, upper);
        if (!double.IsFinite(reportedEffect)
            || !double.IsFinite(reportedLower)
            || !double.IsFinite(reportedUpper)
            || reportedEffect <= 0d
            || reportedLower <= 0d
            || reportedUpper <= 0d)
        {
            reasons.Add(QuantitativeRandomEffectsFailureReason.BackTransformationFailed);
            return CreateRejected(betweenStudyVariance, contributions, reasons);
        }

        var result = new QuantitativeRandomEffectsSynthesisResult(
            QuantitativeSynthesisStatus.Synthesized,
            QuantitativeSynthesisMethod.RandomEffectsInverseVariance,
            AlgorithmVersion,
            QuantitativeConfidenceIntervalMethod.WaldStandardNormal,
            _options.OutputConfidenceLevel,
            tauSquared,
            betweenStudyVariance.Estimator,
            betweenStudyVariance.AlgorithmVersion,
            betweenStudyVariance.StudyCount,
            pooledEffect,
            pooledVariance,
            pooledStandardError,
            lower,
            upper,
            reportedEffect,
            reportedLower,
            reportedUpper,
            HksjInference: null,
            contributions,
            []);

        var hksjInference = new HksjSummaryEffectInferenceCalculator(_options).Calculate(result, effectMeasureType);
        return result with { HksjInference = hksjInference };
    }

    private static IReadOnlyCollection<QuantitativeSynthesisContribution> BuildPreflightContributions(
        IReadOnlyCollection<QuantitativeSynthesisContribution> commonEffectContributions,
        double tauSquared,
        ICollection<QuantitativeRandomEffectsFailureReason> reasons)
    {
        var contributions = new List<QuantitativeSynthesisContribution>();
        var seenEvidenceIds = new HashSet<Guid>();
        var seenStudyIds = new HashSet<Guid>();

        foreach (var contribution in commonEffectContributions)
        {
            if (!seenEvidenceIds.Add(contribution.EvidenceId) || !seenStudyIds.Add(contribution.StudyId))
            {
                reasons.Add(QuantitativeRandomEffectsFailureReason.DuplicateContribution);
                continue;
            }

            if (!double.IsFinite(contribution.AnalysisScaleEffect))
            {
                reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidEffect);
                continue;
            }

            if (!double.IsFinite(contribution.AnalysisScaleVariance) || contribution.AnalysisScaleVariance <= 0d)
            {
                reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidVariance);
                continue;
            }

            var denominator = contribution.AnalysisScaleVariance + tauSquared;
            if (!double.IsFinite(denominator) || denominator <= 0d)
            {
                reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidWeight);
                continue;
            }

            var weight = 1d / denominator;
            if (!double.IsFinite(weight) || weight <= 0d)
            {
                reasons.Add(QuantitativeRandomEffectsFailureReason.InvalidWeight);
                continue;
            }

            contributions.Add(contribution with
            {
                Weight = weight,
                NormalizedWeight = 0d
            });
        }

        return contributions;
    }

    private static double BackTransform(EffectMeasureType effectMeasureType, double value)
    {
        return effectMeasureType is EffectMeasureType.OddsRatio or EffectMeasureType.RiskRatio or EffectMeasureType.HazardRatio
            ? Math.Exp(value)
            : value;
    }

    private QuantitativeRandomEffectsSynthesisResult CreateRejected(
        BetweenStudyVarianceEstimate betweenStudyVariance,
        IReadOnlyCollection<QuantitativeSynthesisContribution> contributions,
        IReadOnlyCollection<QuantitativeRandomEffectsFailureReason> reasons)
    {
        return new QuantitativeRandomEffectsSynthesisResult(
            QuantitativeSynthesisStatus.NotSynthesizable,
            QuantitativeSynthesisMethod.RandomEffectsInverseVariance,
            AlgorithmVersion,
            QuantitativeConfidenceIntervalMethod.WaldStandardNormal,
            _options.OutputConfidenceLevel,
            TauSquared: betweenStudyVariance.TauSquared,
            TauSquaredEstimator: betweenStudyVariance.Estimator,
            TauSquaredAlgorithmVersion: betweenStudyVariance.AlgorithmVersion,
            StudyCount: betweenStudyVariance.StudyCount,
            AnalysisScaleEffect: null,
            AnalysisScaleVariance: null,
            AnalysisScaleStandardError: null,
            AnalysisScaleConfidenceIntervalLower: null,
            AnalysisScaleConfidenceIntervalUpper: null,
            ReportedScaleEffect: null,
            ReportedScaleConfidenceIntervalLower: null,
            ReportedScaleConfidenceIntervalUpper: null,
            HksjInference: null,
            Contributions: contributions,
            FailureReasons: reasons.Distinct().OrderBy(reason => reason).ToArray());
    }
}

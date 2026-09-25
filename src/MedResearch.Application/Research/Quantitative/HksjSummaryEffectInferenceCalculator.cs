namespace MedResearch.Application.Research.Quantitative;

public sealed class HksjSummaryEffectInferenceCalculator
{
    public const string AlgorithmVersion = "random-effects-hksj-summary-inference-v1";

    private readonly QuantitativeSynthesisOptions _options;

    public HksjSummaryEffectInferenceCalculator(QuantitativeSynthesisOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public QuantitativeHksjInferenceResult Calculate(
        QuantitativeRandomEffectsSynthesisResult randomEffects,
        EffectMeasureType effectMeasureType)
    {
        ArgumentNullException.ThrowIfNull(randomEffects);

        if (randomEffects.Status != QuantitativeSynthesisStatus.Synthesized
            || !randomEffects.AnalysisScaleEffect.HasValue
            || !randomEffects.AnalysisScaleVariance.HasValue
            || !randomEffects.ReportedScaleEffect.HasValue)
        {
            return CreateRejected(
                randomEffects.StudyCount,
                null,
                [QuantitativeHksjFailureReason.RandomEffectsNotSynthesized]);
        }

        var studyCount = randomEffects.Contributions.Count;
        var degreesOfFreedom = studyCount - 1;
        if (degreesOfFreedom <= 0)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                [QuantitativeHksjFailureReason.InsufficientDegreesOfFreedom]);
        }

        var pooledEffect = randomEffects.AnalysisScaleEffect.Value;
        var waldVariance = randomEffects.AnalysisScaleVariance.Value;
        if (!double.IsFinite(waldVariance) || waldVariance <= 0d)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                [QuantitativeHksjFailureReason.InvalidWaldVariance]);
        }

        var reasons = new List<QuantitativeHksjFailureReason>();
        var weightedResidualSum = 0d;
        foreach (var contribution in randomEffects.Contributions)
        {
            if (!double.IsFinite(contribution.AnalysisScaleEffect))
            {
                reasons.Add(QuantitativeHksjFailureReason.InvalidEffect);
                continue;
            }

            if (!double.IsFinite(contribution.Weight) || contribution.Weight <= 0d)
            {
                reasons.Add(QuantitativeHksjFailureReason.InvalidWeight);
                continue;
            }

            var residual = contribution.AnalysisScaleEffect - pooledEffect;
            var term = contribution.Weight * residual * residual;
            if (!double.IsFinite(term) || term < 0d)
            {
                reasons.Add(QuantitativeHksjFailureReason.NonFiniteVarianceAdjustment);
                continue;
            }

            weightedResidualSum += term;
        }

        if (reasons.Count > 0)
        {
            return CreateRejected(studyCount, degreesOfFreedom, reasons);
        }

        var varianceAdjustment = weightedResidualSum / degreesOfFreedom;
        var hksjVariance = varianceAdjustment * waldVariance;
        var hksjStandardError = Math.Sqrt(hksjVariance);
        if (!double.IsFinite(varianceAdjustment)
            || varianceAdjustment < 0d
            || !double.IsFinite(hksjVariance)
            || hksjVariance < 0d
            || !double.IsFinite(hksjStandardError)
            || hksjStandardError < 0d)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                [QuantitativeHksjFailureReason.NonFiniteVarianceAdjustment]);
        }

        var t = StudentTQuantile.Inverse(0.5d + _options.BoundedOutputConfidenceLevel / 2d, degreesOfFreedom);
        var lower = pooledEffect - t * hksjStandardError;
        var upper = pooledEffect + t * hksjStandardError;
        if (!double.IsFinite(t) || t <= 0d || !double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                [QuantitativeHksjFailureReason.NonFiniteConfidenceInterval]);
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
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                [QuantitativeHksjFailureReason.BackTransformationFailed]);
        }

        return new QuantitativeHksjInferenceResult(
            QuantitativeSynthesisStatus.Synthesized,
            QuantitativeConfidenceIntervalMethod.HartungKnappSidikJonkman,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            studyCount,
            degreesOfFreedom,
            varianceAdjustment,
            t,
            pooledEffect,
            hksjVariance,
            hksjStandardError,
            lower,
            upper,
            reportedEffect,
            reportedLower,
            reportedUpper,
            []);
    }

    private QuantitativeHksjInferenceResult CreateRejected(
        int studyCount,
        int? degreesOfFreedom,
        IReadOnlyCollection<QuantitativeHksjFailureReason> reasons)
    {
        return new QuantitativeHksjInferenceResult(
            QuantitativeSynthesisStatus.NotSynthesizable,
            QuantitativeConfidenceIntervalMethod.HartungKnappSidikJonkman,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            studyCount,
            degreesOfFreedom,
            VarianceAdjustment: null,
            CriticalValue: null,
            AnalysisScaleEffect: null,
            AnalysisScaleVariance: null,
            AnalysisScaleStandardError: null,
            AnalysisScaleConfidenceIntervalLower: null,
            AnalysisScaleConfidenceIntervalUpper: null,
            ReportedScaleEffect: null,
            ReportedScaleConfidenceIntervalLower: null,
            ReportedScaleConfidenceIntervalUpper: null,
            FailureReasons: reasons.Distinct().OrderBy(reason => reason).ToArray());
    }

    private static double BackTransform(EffectMeasureType effectMeasureType, double value)
    {
        return effectMeasureType is EffectMeasureType.OddsRatio or EffectMeasureType.RiskRatio or EffectMeasureType.HazardRatio
            ? Math.Exp(value)
            : value;
    }
}

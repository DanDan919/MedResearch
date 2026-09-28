namespace MedResearch.Application.Research.Quantitative;

public sealed class RandomEffectsPredictionIntervalCalculator
{
    public const string AlgorithmVersion = "random-effects-prediction-interval-cochrane-t-v1";

    private readonly QuantitativeSynthesisOptions _options;

    public RandomEffectsPredictionIntervalCalculator(QuantitativeSynthesisOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public QuantitativePredictionIntervalResult Calculate(
        QuantitativeRandomEffectsSynthesisResult randomEffects,
        EffectMeasureType effectMeasureType)
    {
        ArgumentNullException.ThrowIfNull(randomEffects);

        var studyCount = randomEffects.Contributions.Count;
        var degreesOfFreedom = studyCount - 1;
        if (randomEffects.Status != QuantitativeSynthesisStatus.Synthesized)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                randomEffects.TauSquared,
                randomEffects.AnalysisScaleVariance,
                [QuantitativePredictionIntervalFailureReason.RandomEffectsNotSynthesized]);
        }

        if (degreesOfFreedom <= 0)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                randomEffects.TauSquared,
                randomEffects.AnalysisScaleVariance,
                [QuantitativePredictionIntervalFailureReason.InsufficientDegreesOfFreedom]);
        }

        if (!randomEffects.TauSquared.HasValue
            || !double.IsFinite(randomEffects.TauSquared.Value)
            || randomEffects.TauSquared.Value < 0d)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                randomEffects.TauSquared,
                randomEffects.AnalysisScaleVariance,
                [QuantitativePredictionIntervalFailureReason.InvalidTauSquared]);
        }

        if (!randomEffects.AnalysisScaleEffect.HasValue
            || !double.IsFinite(randomEffects.AnalysisScaleEffect.Value)
            || !randomEffects.AnalysisScaleVariance.HasValue
            || !double.IsFinite(randomEffects.AnalysisScaleVariance.Value)
            || randomEffects.AnalysisScaleVariance.Value <= 0d)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                randomEffects.TauSquared,
                randomEffects.AnalysisScaleVariance,
                [QuantitativePredictionIntervalFailureReason.InvalidSummaryVariance]);
        }

        var tauSquared = randomEffects.TauSquared.Value;
        var summaryVariance = randomEffects.AnalysisScaleVariance.Value;
        var summaryStandardError = Math.Sqrt(summaryVariance);
        var predictionVariance = summaryVariance + tauSquared;
        var predictionStandardError = Math.Sqrt(predictionVariance);
        if (!double.IsFinite(summaryStandardError)
            || summaryStandardError <= 0d
            || !double.IsFinite(predictionVariance)
            || predictionVariance <= 0d
            || !double.IsFinite(predictionStandardError)
            || predictionStandardError <= 0d)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                tauSquared,
                summaryVariance,
                [QuantitativePredictionIntervalFailureReason.NonFinitePredictionVariance]);
        }

        var critical = StudentTQuantile.Inverse(0.5d + _options.BoundedOutputConfidenceLevel / 2d, degreesOfFreedom);
        var lower = randomEffects.AnalysisScaleEffect.Value - critical * predictionStandardError;
        var upper = randomEffects.AnalysisScaleEffect.Value + critical * predictionStandardError;
        if (!double.IsFinite(critical)
            || critical <= 0d
            || !double.IsFinite(lower)
            || !double.IsFinite(upper)
            || lower > upper)
        {
            return CreateRejected(
                studyCount,
                degreesOfFreedom,
                tauSquared,
                summaryVariance,
                [QuantitativePredictionIntervalFailureReason.NonFinitePredictionInterval]);
        }

        var reportedEffect = BackTransform(effectMeasureType, randomEffects.AnalysisScaleEffect.Value);
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
                tauSquared,
                summaryVariance,
                [QuantitativePredictionIntervalFailureReason.BackTransformationFailed]);
        }

        return new QuantitativePredictionIntervalResult(
            QuantitativeSynthesisStatus.Synthesized,
            QuantitativePredictionIntervalMethod.CochraneRandomEffectsStudentT,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            studyCount,
            degreesOfFreedom,
            tauSquared,
            summaryVariance,
            summaryStandardError,
            predictionVariance,
            predictionStandardError,
            critical,
            randomEffects.AnalysisScaleEffect.Value,
            lower,
            upper,
            reportedEffect,
            reportedLower,
            reportedUpper,
            []);
    }

    private QuantitativePredictionIntervalResult CreateRejected(
        int studyCount,
        int degreesOfFreedom,
        double? tauSquared,
        double? summaryVariance,
        IReadOnlyCollection<QuantitativePredictionIntervalFailureReason> reasons)
    {
        double? summaryStandardError = null;
        if (summaryVariance.HasValue
            && double.IsFinite(summaryVariance.Value)
            && summaryVariance.Value > 0d)
        {
            summaryStandardError = Math.Sqrt(summaryVariance.Value);
        }

        return new QuantitativePredictionIntervalResult(
            QuantitativeSynthesisStatus.NotSynthesizable,
            QuantitativePredictionIntervalMethod.CochraneRandomEffectsStudentT,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            studyCount,
            degreesOfFreedom,
            tauSquared,
            summaryVariance,
            summaryStandardError,
            PredictionVariance: null,
            PredictionStandardError: null,
            CriticalValue: null,
            AnalysisScaleEffect: null,
            AnalysisScaleLower: null,
            AnalysisScaleUpper: null,
            ReportedScaleEffect: null,
            ReportedScaleLower: null,
            ReportedScaleUpper: null,
            FailureReasons: reasons.Distinct().OrderBy(reason => reason).ToArray());
    }

    private static double BackTransform(EffectMeasureType effectMeasureType, double value)
    {
        return effectMeasureType is EffectMeasureType.OddsRatio or EffectMeasureType.RiskRatio or EffectMeasureType.HazardRatio
            ? Math.Exp(value)
            : value;
    }
}

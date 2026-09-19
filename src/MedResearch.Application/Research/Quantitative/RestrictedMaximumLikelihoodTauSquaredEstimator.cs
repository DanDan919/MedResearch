namespace MedResearch.Application.Research.Quantitative;

public static class RestrictedMaximumLikelihoodTauSquaredEstimator
{
    public const string AlgorithmVersion = "reml-tau-squared-v1";

    private const int MaxBracketExpansions = 100;
    private const int MaxBisectionIterations = 200;
    private const double ScoreTolerance = 1e-10d;
    private const double ParameterTolerance = 1e-10d;
    private const double MinimumUpperBound = 1e-12d;
    private const double MaximumUpperBound = 1e12d;
    private const double BoundaryTolerance = 1e-12d;

    public static BetweenStudyVarianceEstimate Estimate(IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var ordered = contributions
            .OrderBy(contribution => contribution.StudyId)
            .ThenBy(contribution => contribution.EvidenceId)
            .ToArray();

        if (ordered.Length < 2)
        {
            return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.InsufficientIndependentStudies, 0);
        }

        if (ordered.Any(contribution => !IsValidEffectVariancePair(contribution)))
        {
            return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.InvalidInput, 0);
        }

        var lowerScore = TryCalculateScore(ordered, 0d);
        if (!lowerScore.HasValue)
        {
            return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.NonFiniteCalculation, 0);
        }

        if (lowerScore.Value <= BoundaryTolerance)
        {
            return Estimated(0d, ordered.Length, 0);
        }

        var upper = InitialUpperBound(ordered);
        var upperScore = TryCalculateScore(ordered, upper);
        var bracketIterations = 0;
        while (upperScore.HasValue && upperScore.Value > 0d && bracketIterations < MaxBracketExpansions)
        {
            if (upper >= MaximumUpperBound / 2d)
            {
                return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.FailedToBracket, bracketIterations);
            }

            upper *= 2d;
            bracketIterations++;
            upperScore = TryCalculateScore(ordered, upper);
        }

        if (!upperScore.HasValue)
        {
            return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.NonFiniteCalculation, bracketIterations);
        }

        if (upperScore.Value > 0d)
        {
            return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.FailedToBracket, bracketIterations);
        }

        var lower = 0d;
        for (var iteration = 1; iteration <= MaxBisectionIterations; iteration++)
        {
            var midpoint = lower + (upper - lower) / 2d;
            var score = TryCalculateScore(ordered, midpoint);
            if (!score.HasValue)
            {
                return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.NonFiniteCalculation, bracketIterations + iteration);
            }

            if (Math.Abs(score.Value) <= ScoreTolerance)
            {
                return Estimated(midpoint, ordered.Length, bracketIterations + iteration);
            }

            if (score.Value > 0d)
            {
                lower = midpoint;
            }
            else
            {
                upper = midpoint;
            }

            if ((upper - lower) <= ParameterTolerance * Math.Max(1d, midpoint))
            {
                return Estimated(lower + (upper - lower) / 2d, ordered.Length, bracketIterations + iteration);
            }
        }

        return NotEstimated(ordered.Length, BetweenStudyVarianceFailureReason.MaxIterationsExceeded, bracketIterations + MaxBisectionIterations);
    }

    private static double? TryCalculateScore(IReadOnlyCollection<QuantitativeSynthesisContribution> contributions, double tauSquared)
    {
        if (!double.IsFinite(tauSquared) || tauSquared < 0d)
        {
            return null;
        }

        var sumWeight = 0d;
        var sumWeightedEffect = 0d;
        foreach (var contribution in contributions)
        {
            var denominator = contribution.AnalysisScaleVariance + tauSquared;
            if (!double.IsFinite(denominator) || denominator <= 0d)
            {
                return null;
            }

            var weight = 1d / denominator;
            var weightedEffect = weight * contribution.AnalysisScaleEffect;
            if (!double.IsFinite(weight) || weight <= 0d || !double.IsFinite(weightedEffect))
            {
                return null;
            }

            sumWeight += weight;
            sumWeightedEffect += weightedEffect;
        }

        if (!double.IsFinite(sumWeight) || sumWeight <= 0d || !double.IsFinite(sumWeightedEffect))
        {
            return null;
        }

        var mean = sumWeightedEffect / sumWeight;
        if (!double.IsFinite(mean))
        {
            return null;
        }

        var weightedSquaredResidualSum = 0d;
        var squaredWeightSum = 0d;
        foreach (var contribution in contributions)
        {
            var weight = 1d / (contribution.AnalysisScaleVariance + tauSquared);
            var squaredWeight = weight * weight;
            var residual = contribution.AnalysisScaleEffect - mean;
            var term = squaredWeight * residual * residual;
            if (!double.IsFinite(squaredWeight) || !double.IsFinite(residual) || !double.IsFinite(term))
            {
                return null;
            }

            squaredWeightSum += squaredWeight;
            weightedSquaredResidualSum += term;
        }

        var score = weightedSquaredResidualSum - sumWeight + squaredWeightSum / sumWeight;
        return double.IsFinite(score) ? score : null;
    }

    private static double InitialUpperBound(IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        var maxVariance = contributions.Max(contribution => contribution.AnalysisScaleVariance);
        var meanEffect = contributions.Average(contribution => contribution.AnalysisScaleEffect);
        var observedVariance = contributions.Sum(contribution => Math.Pow(contribution.AnalysisScaleEffect - meanEffect, 2d)) / (contributions.Count - 1);
        var upper = Math.Max(MinimumUpperBound, Math.Max(maxVariance, observedVariance));
        return double.IsFinite(upper) && upper > 0d ? upper : MinimumUpperBound;
    }

    private static bool IsValidEffectVariancePair(QuantitativeSynthesisContribution contribution)
    {
        return double.IsFinite(contribution.AnalysisScaleEffect)
            && double.IsFinite(contribution.AnalysisScaleVariance)
            && contribution.AnalysisScaleVariance > 0d;
    }

    private static BetweenStudyVarianceEstimate Estimated(double tauSquared, int studyCount, int iterationCount)
    {
        if (!double.IsFinite(tauSquared) || tauSquared < 0d)
        {
            return NotEstimated(studyCount, BetweenStudyVarianceFailureReason.NonFiniteCalculation, iterationCount);
        }

        if (tauSquared < 0d && tauSquared >= -BoundaryTolerance)
        {
            tauSquared = 0d;
        }

        return new BetweenStudyVarianceEstimate(
            tauSquared,
            BetweenStudyVarianceEstimator.RestrictedMaximumLikelihood,
            BetweenStudyVarianceEstimateStatus.Estimated,
            AlgorithmVersion,
            studyCount,
            true,
            iterationCount,
            null);
    }

    private static BetweenStudyVarianceEstimate NotEstimated(int studyCount, BetweenStudyVarianceFailureReason reason, int iterationCount)
    {
        return new BetweenStudyVarianceEstimate(
            null,
            BetweenStudyVarianceEstimator.RestrictedMaximumLikelihood,
            BetweenStudyVarianceEstimateStatus.NotEstimated,
            AlgorithmVersion,
            studyCount,
            false,
            iterationCount,
            reason);
    }
}

namespace MedResearch.Application.Research.Quantitative;

public static class HeterogeneityDiagnosticsCalculator
{
    public const string AlgorithmVersion = "cochran-q-i2-v1";

    private const double NumericalZeroTolerance = 1e-12d;

    public static QuantitativeHeterogeneityDiagnostics Calculate(
        IReadOnlyCollection<QuantitativeSynthesisContribution> contributions,
        double pooledAnalysisScaleEffect)
    {
        ArgumentNullException.ThrowIfNull(contributions);

        if (contributions.Count < 2)
        {
            throw new InvalidOperationException("Heterogeneity diagnostics require at least two independent study contributions.");
        }

        if (!double.IsFinite(pooledAnalysisScaleEffect))
        {
            throw new InvalidOperationException("Heterogeneity diagnostics require a finite pooled analysis-scale effect.");
        }

        var q = 0d;
        foreach (var contribution in contributions)
        {
            if (!double.IsFinite(contribution.AnalysisScaleEffect)
                || !double.IsFinite(contribution.Weight)
                || contribution.Weight <= 0)
            {
                throw new InvalidOperationException("Heterogeneity diagnostics require finite analysis-scale effects and positive finite weights.");
            }

            var deviation = contribution.AnalysisScaleEffect - pooledAnalysisScaleEffect;
            var weightedSquaredDeviation = contribution.Weight * deviation * deviation;
            if (!double.IsFinite(weightedSquaredDeviation) || weightedSquaredDeviation < -NumericalZeroTolerance)
            {
                throw new InvalidOperationException("Cochran's Q calculation produced a non-finite or materially negative term.");
            }

            q += weightedSquaredDeviation;
        }

        if (!double.IsFinite(q))
        {
            throw new InvalidOperationException("Cochran's Q calculation produced a non-finite result.");
        }

        if (q < 0 && q >= -NumericalZeroTolerance)
        {
            q = 0d;
        }

        if (q < 0)
        {
            throw new InvalidOperationException("Cochran's Q calculation produced a materially negative result.");
        }

        var df = contributions.Count - 1;
        var iSquared = CalculateISquared(q, df);

        return new QuantitativeHeterogeneityDiagnostics(
            q,
            df,
            iSquared,
            contributions.Count,
            AlgorithmVersion);
    }

    private static double CalculateISquared(double q, int degreesOfFreedom)
    {
        if (degreesOfFreedom < 1)
        {
            throw new InvalidOperationException("I-squared requires at least one degree of freedom.");
        }

        if (q <= NumericalZeroTolerance || q <= degreesOfFreedom)
        {
            return 0d;
        }

        var value = (q - degreesOfFreedom) / q;
        if (!double.IsFinite(value) || value < -NumericalZeroTolerance || value > 1d + NumericalZeroTolerance)
        {
            throw new InvalidOperationException("I-squared calculation produced a value outside the valid range.");
        }

        if (value < 0)
        {
            return 0d;
        }

        return value > 1d ? 1d : value;
    }
}

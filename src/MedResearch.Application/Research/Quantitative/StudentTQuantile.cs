namespace MedResearch.Application.Research.Quantitative;

internal static class StudentTQuantile
{
    public static double Inverse(double probability, int degreesOfFreedom)
    {
        if (probability <= 0d || probability >= 1d || degreesOfFreedom <= 0)
        {
            return double.NaN;
        }

        if (probability == 0.5d)
        {
            return 0d;
        }

        var sign = probability < 0.5d ? -1d : 1d;
        var target = probability < 0.5d ? 1d - probability : probability;
        var lower = 0d;
        var upper = 1d;

        while (Cdf(upper, degreesOfFreedom) < target)
        {
            upper *= 2d;
            if (!double.IsFinite(upper) || upper > 1e12d)
            {
                return double.NaN;
            }
        }

        for (var i = 0; i < 160; i++)
        {
            var midpoint = lower + (upper - lower) / 2d;
            if (Cdf(midpoint, degreesOfFreedom) < target)
            {
                lower = midpoint;
            }
            else
            {
                upper = midpoint;
            }
        }

        return sign * (lower + (upper - lower) / 2d);
    }

    private static double Cdf(double value, int degreesOfFreedom)
    {
        if (value == 0d)
        {
            return 0.5d;
        }

        var x = degreesOfFreedom / (degreesOfFreedom + value * value);
        var ibeta = RegularizedIncompleteBeta(degreesOfFreedom / 2d, 0.5d, x);
        return value > 0d
            ? 1d - 0.5d * ibeta
            : 0.5d * ibeta;
    }

    private static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0d)
        {
            return 0d;
        }

        if (x >= 1d)
        {
            return 1d;
        }

        var logBetaTerm = LogGamma(a + b) - LogGamma(a) - LogGamma(b)
            + a * Math.Log(x) + b * Math.Log(1d - x);
        var betaTerm = Math.Exp(logBetaTerm);

        if (x < (a + 1d) / (a + b + 2d))
        {
            return betaTerm * BetaContinuedFraction(a, b, x) / a;
        }

        return 1d - betaTerm * BetaContinuedFraction(b, a, 1d - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const int maxIterations = 200;
        const double epsilon = 3e-14d;
        const double fpmin = 1e-300d;

        var qab = a + b;
        var qap = a + 1d;
        var qam = a - 1d;
        var c = 1d;
        var d = 1d - qab * x / qap;
        if (Math.Abs(d) < fpmin)
        {
            d = fpmin;
        }

        d = 1d / d;
        var h = d;

        for (var m = 1; m <= maxIterations; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1d + aa * d;
            if (Math.Abs(d) < fpmin)
            {
                d = fpmin;
            }

            c = 1d + aa / c;
            if (Math.Abs(c) < fpmin)
            {
                c = fpmin;
            }

            d = 1d / d;
            h *= d * c;

            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1d + aa * d;
            if (Math.Abs(d) < fpmin)
            {
                d = fpmin;
            }

            c = 1d + aa / c;
            if (Math.Abs(c) < fpmin)
            {
                c = fpmin;
            }

            d = 1d / d;
            var delta = d * c;
            h *= delta;

            if (Math.Abs(delta - 1d) < epsilon)
            {
                return h;
            }
        }

        return double.NaN;
    }

    private static double LogGamma(double value)
    {
        double[] coefficients =
        [
            676.5203681218851d,
            -1259.1392167224028d,
            771.32342877765313d,
            -176.61502916214059d,
            12.507343278686905d,
            -0.13857109526572012d,
            9.9843695780195716e-6d,
            1.5056327351493116e-7d
        ];

        if (value < 0.5d)
        {
            return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * value)) - LogGamma(1d - value);
        }

        value -= 1d;
        var x = 0.99999999999980993d;
        for (var i = 0; i < coefficients.Length; i++)
        {
            x += coefficients[i] / (value + i + 1d);
        }

        var t = value + coefficients.Length - 0.5d;
        return 0.5d * Math.Log(2d * Math.PI) + (value + 0.5d) * Math.Log(t) - t + Math.Log(x);
    }
}

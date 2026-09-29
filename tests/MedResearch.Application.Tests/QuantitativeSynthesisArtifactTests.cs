using MedResearch.Application.Research.Quantitative;

namespace MedResearch.Application.Tests;

public sealed class QuantitativeSynthesisArtifactTests
{
    [Fact]
    public void Fingerprint_IsStableWhenContributionOrderChanges()
    {
        var first = CreateResult([
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("22222222-2222-2222-2222-222222222222"))]);
        var second = first with { Contributions = first.Contributions.Reverse().ToArray() };

        Assert.Equal(
            QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(first),
            QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(second));
    }

    [Fact]
    public void Fingerprint_RejectsNonFiniteValues()
    {
        var result = CreateResult([CreateContribution(Guid.NewGuid(), Guid.NewGuid()) with { Weight = double.NaN }]);

        Assert.Throws<InvalidOperationException>(() => QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result));
    }

    private static QuantitativeSynthesisResult CreateResult(IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        return new QuantitativeSynthesisResult(
            Guid.NewGuid(),
            "outcome|population|comparator|design|OddsRatio",
            "outcome",
            "population",
            "comparator",
            "design",
            EffectMeasureType.OddsRatio,
            QuantitativeSynthesisStatus.Synthesized,
            QuantitativeSynthesisMethod.FixedEffectInverseVariance,
            "fixed-effect-inverse-variance-v1",
            0.95m,
            contributions.Count,
            contributions.Count,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            -1d,
            1.5d,
            Math.Exp(0.25d),
            Math.Exp(-1d),
            Math.Exp(1.5d),
            null,
            null,
            null,
            contributions,
            []);
    }

    private static QuantitativeSynthesisContribution CreateContribution(Guid studyId, Guid evidenceId)
    {
        return new QuantitativeSynthesisContribution(
            evidenceId,
            studyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.25d,
            1d,
            1d,
            1d,
            0.5d);
    }
}

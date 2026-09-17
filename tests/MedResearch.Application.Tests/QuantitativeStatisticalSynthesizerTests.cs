using MedResearch.Application.Research.Quantitative;

namespace MedResearch.Application.Tests;

public sealed class QuantitativeStatisticalSynthesizerTests
{
    [Fact]
    public void Synthesize_PoolsOddsRatiosOnLogScaleInsteadOfAveragingRawRatios()
    {
        var runId = Guid.NewGuid();
        var studyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var studyB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var evidenceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var evidenceB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, studyA, evidenceA, Math.Log(2d), 0.04d),
            CreateAssessment(runId, studyB, evidenceB, Math.Log(8d), 0.04d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.Synthesized, result.Status);
        Assert.Equal(QuantitativeSynthesisMethod.FixedEffectInverseVariance, result.Method);
        Assert.Equal(Math.Log(4d), result.AnalysisScaleEffect!.Value, 10);
        Assert.Equal(0.02d, result.AnalysisScaleVariance!.Value, 10);
        Assert.Equal(Math.Sqrt(0.02d), result.AnalysisScaleStandardError!.Value, 10);
        Assert.Equal(4d, result.ReportedScaleEffect!.Value, 10);
        Assert.NotEqual(5d, result.ReportedScaleEffect.Value);
        Assert.All(result.Contributions, contribution => Assert.Equal(0.5d, contribution.NormalizedWeight, 10));
    }

    [Fact]
    public void Synthesize_UsesInverseVarianceWeights()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), Math.Log(2d), 0.04d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), Math.Log(8d), 0.16d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        var expected = ((1d / 0.04d) * Math.Log(2d) + (1d / 0.16d) * Math.Log(8d)) / ((1d / 0.04d) + (1d / 0.16d));
        Assert.Equal(expected, result.AnalysisScaleEffect!.Value, 10);
        Assert.Equal(Math.Exp(expected), result.ReportedScaleEffect!.Value, 10);
        Assert.Equal(0.8d, result.Contributions.OrderBy(contribution => contribution.StudyId).First().NormalizedWeight, 10);
        Assert.Equal(0.2d, result.Contributions.OrderBy(contribution => contribution.StudyId).Last().NormalizedWeight, 10);
    }

    [Fact]
    public void Synthesize_RejectsSingleStudyGroup()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var assessment = CreateAssessment(runId, studyId, Guid.NewGuid(), Math.Log(2d), 0.04d);
        var readiness = CreateReadiness(runId, [assessment], ready: false);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.NotSynthesizable, result.Status);
        Assert.Contains(QuantitativeSynthesisRejectionReason.InsufficientIndependentStudies, result.RejectionReasons);
        Assert.Null(result.ReportedScaleEffect);
    }

    [Fact]
    public void Synthesize_RejectsDependentEvidenceFromSameStudy()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, studyId, Guid.NewGuid(), Math.Log(2d), 0.04d),
            CreateAssessment(runId, studyId, Guid.NewGuid(), Math.Log(3d), 0.04d)
        ], ready: false, hasDependentEvidence: true);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.NotSynthesizable, result.Status);
        Assert.Contains(QuantitativeSynthesisRejectionReason.DependentEvidenceFromSameStudy, result.RejectionReasons);
    }

    [Fact]
    public void Synthesize_RejectsUnsupportedEffectMeasureFamiliesInV1()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), 0.5d, 0.04d, EffectMeasureType.MeanDifference),
            CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), 0.7d, 0.04d, EffectMeasureType.MeanDifference)
        ], effectMeasureType: EffectMeasureType.MeanDifference);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.NotSynthesizable, result.Status);
        Assert.Contains(QuantitativeSynthesisRejectionReason.UnsupportedEffectMeasure, result.RejectionReasons);
    }

    [Fact]
    public void Synthesize_RejectsMissingOrInvalidVariance()
    {
        var runId = Guid.NewGuid();
        var valid = CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), Math.Log(2d), 0.04d);
        var missingVariance = CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), Math.Log(3d), 0.04d) with
        {
            Variance = null,
            StandardError = null
        };
        var readiness = CreateReadiness(runId, [valid, missingVariance]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.NotSynthesizable, result.Status);
        Assert.Contains(QuantitativeSynthesisRejectionReason.MissingVariance, result.RejectionReasons);
    }

    [Theory]
    [InlineData(0.90, 1.64485362695147)]
    [InlineData(0.95, 1.95996398454005)]
    [InlineData(0.99, 2.57582930354890)]
    public void Synthesize_UsesConfiguredTwoSidedConfidenceLevel(double confidenceLevel, double expectedCriticalValue)
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), Math.Log(2d), 0.04d),
            CreateAssessment(runId, Guid.NewGuid(), Guid.NewGuid(), Math.Log(8d), 0.04d)
        ]);
        var configuredConfidenceLevel = (decimal)confidenceLevel;
        var synthesizer = CreateSynthesizer(new QuantitativeSynthesisOptions { OutputConfidenceLevel = configuredConfidenceLevel });

        var result = Assert.Single(synthesizer.Synthesize(readiness).Results);

        var expectedHalfWidth = expectedCriticalValue * Math.Sqrt(0.02d);
        Assert.Equal(expectedHalfWidth, result.AnalysisScaleConfidenceIntervalUpper!.Value - result.AnalysisScaleEffect!.Value, 1e-9);
        Assert.Equal(configuredConfidenceLevel, result.OutputConfidenceLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.5)]
    public void Options_RejectInvalidConfidenceLevels(double confidenceLevel)
    {
        var options = new QuantitativeSynthesisOptions { OutputConfidenceLevel = (decimal)confidenceLevel };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    private static FixedEffectQuantitativeStatisticalSynthesizer CreateSynthesizer(QuantitativeSynthesisOptions? options = null)
    {
        return new FixedEffectQuantitativeStatisticalSynthesizer(options ?? new QuantitativeSynthesisOptions());
    }

    private static QuantitativeEvidenceReadiness CreateReadiness(
        Guid runId,
        IReadOnlyCollection<QuantitativeEvidenceAssessment> assessments,
        EffectMeasureType effectMeasureType = EffectMeasureType.OddsRatio,
        bool ready = true,
        bool hasDependentEvidence = false)
    {
        var ordered = assessments.OrderBy(assessment => assessment.StudyId).ThenBy(assessment => assessment.EvidenceId).ToArray();
        var studyIds = ordered.Select(assessment => assessment.StudyId).Distinct().OrderBy(id => id).ToArray();
        var group = new CompatibleEvidenceGroup(
            "depression severity|adults|placebo|randomized controlled trial|" + effectMeasureType,
            "depression severity",
            "adults",
            "placebo",
            "randomized controlled trial",
            effectMeasureType,
            ordered.Select(assessment => assessment.EvidenceId).ToArray(),
            studyIds,
            ordered.Length,
            studyIds.Length,
            hasDependentEvidence,
            ready);

        return new QuantitativeEvidenceReadiness(
            runId,
            ordered,
            [group],
            ordered.Count(assessment => assessment.Eligibility == QuantitativeEligibility.Eligible),
            ordered.Count(assessment => assessment.Eligibility == QuantitativeEligibility.Ineligible),
            QuantitativeEvidenceAssessor.AlgorithmVersion);
    }

    private static QuantitativeEvidenceAssessment CreateAssessment(
        Guid runId,
        Guid studyId,
        Guid evidenceId,
        double analysisScaleEffect,
        double variance,
        EffectMeasureType effectMeasureType = EffectMeasureType.OddsRatio)
    {
        return new QuantitativeEvidenceAssessment(
            evidenceId,
            runId,
            studyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "depression severity",
            "adults",
            "placebo",
            "randomized controlled trial",
            effectMeasureType == EffectMeasureType.MeanDifference ? "mean difference" : "odds ratio",
            effectMeasureType,
            QuantitativeEligibility.Eligible,
            (decimal)Math.Exp(analysisScaleEffect),
            analysisScaleEffect,
            Math.Sqrt(variance),
            variance,
            StatisticOrigin.Reported,
            StatisticOrigin.Reported,
            false,
            []);
    }
}

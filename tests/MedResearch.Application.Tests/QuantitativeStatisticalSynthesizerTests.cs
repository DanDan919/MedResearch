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


    [Fact]
    public void Synthesize_AddsCochranQDegreesOfFreedomAndISquaredForSameContributionSet()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), 0d, 1d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), 2d, 1d),
            CreateAssessment(runId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Guid.NewGuid(), 4d, 1d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(2d, result.AnalysisScaleEffect!.Value, 12);
        Assert.Equal(1d / 3d, result.AnalysisScaleVariance!.Value, 12);
        Assert.NotNull(result.HeterogeneityDiagnostics);
        var diagnostics = result.HeterogeneityDiagnostics!;
        Assert.Equal(HeterogeneityDiagnosticsCalculator.AlgorithmVersion, diagnostics.AlgorithmVersion);
        Assert.Equal(8d, diagnostics.CochransQ, 12);
        Assert.Equal(2, diagnostics.DegreesOfFreedom);
        Assert.Equal(0.75d, diagnostics.ISquared, 12);
        Assert.Equal(3, diagnostics.StudyCount);
    }

    [Fact]
    public void Synthesize_CalculatesHeterogeneityOnAnalysisScaleNotRawRatioScale()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), 0d, 1d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), 2d, 1d),
            CreateAssessment(runId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Guid.NewGuid(), 4d, 1d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(8d, result.HeterogeneityDiagnostics!.CochransQ, 12);
        var rawRatioMean = (1d + Math.Exp(2d) + Math.Exp(4d)) / 3d;
        var rawRatioQ = Math.Pow(1d - rawRatioMean, 2d)
            + Math.Pow(Math.Exp(2d) - rawRatioMean, 2d)
            + Math.Pow(Math.Exp(4d) - rawRatioMean, 2d);
        Assert.NotEqual(rawRatioQ, result.HeterogeneityDiagnostics.CochransQ);
    }

    [Fact]
    public void Synthesize_BoundsISquaredAtZeroWhenQIsLessThanOrEqualToDegreesOfFreedom()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), 0d, 1d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), 1d, 1d),
            CreateAssessment(runId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Guid.NewGuid(), 2d, 1d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(2d, result.HeterogeneityDiagnostics!.CochransQ, 12);
        Assert.Equal(2, result.HeterogeneityDiagnostics.DegreesOfFreedom);
        Assert.Equal(0d, result.HeterogeneityDiagnostics.ISquared, 12);
    }

    [Fact]
    public void Synthesize_HandlesZeroQWithoutDivisionByZero()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), Math.Log(2d), 0.04d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), Math.Log(2d), 0.04d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(0d, result.HeterogeneityDiagnostics!.CochransQ, 12);
        Assert.Equal(1, result.HeterogeneityDiagnostics.DegreesOfFreedom);
        Assert.Equal(0d, result.HeterogeneityDiagnostics.ISquared, 12);
        Assert.False(double.IsNaN(result.HeterogeneityDiagnostics.ISquared));
        Assert.False(double.IsInfinity(result.HeterogeneityDiagnostics.ISquared));
    }

    [Fact]
    public void Synthesize_UsesKMinusOneDegreesOfFreedomForTwoStudies()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(runId, [
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), 0d, 1d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), 4d, 1d)
        ]);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(8d, result.HeterogeneityDiagnostics!.CochransQ, 12);
        Assert.Equal(1, result.HeterogeneityDiagnostics.DegreesOfFreedom);
        Assert.Equal(0.875d, result.HeterogeneityDiagnostics.ISquared, 12);
    }

    [Fact]
    public void Synthesize_HeterogeneityDiagnosticsAreOrderIndependent()
    {
        var runId = Guid.NewGuid();
        var assessments = new[]
        {
            CreateAssessment(runId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), 0d, 1d),
            CreateAssessment(runId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.NewGuid(), 2d, 1d),
            CreateAssessment(runId, Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Guid.NewGuid(), 4d, 1d)
        };

        var first = Assert.Single(CreateSynthesizer().Synthesize(CreateReadiness(runId, assessments)).Results);
        var second = Assert.Single(CreateSynthesizer().Synthesize(CreateReadiness(runId, assessments.Reverse().ToArray())).Results);

        Assert.Equal(first.AnalysisScaleEffect, second.AnalysisScaleEffect);
        Assert.Equal(first.HeterogeneityDiagnostics, second.HeterogeneityDiagnostics);
    }

    [Fact]
    public void Calculate_RejectsNonFiniteInputs()
    {
        var contributions = new[]
        {
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1d, 1d),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), double.NaN, 1d)
        };

        Assert.Throws<InvalidOperationException>(() => HeterogeneityDiagnosticsCalculator.Calculate(contributions, 1d));
    }

    [Fact]
    public void Calculate_RejectsNonPositiveWeights()
    {
        var contributions = new[]
        {
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1d, 1d),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 2d, 0d)
        };

        Assert.Throws<InvalidOperationException>(() => HeterogeneityDiagnosticsCalculator.Calculate(contributions, 1.5d));
    }

    [Fact]
    public void Synthesize_AddsRestrictedMaximumLikelihoodTauSquaredForBcgReferenceDataset()
    {
        var runId = Guid.NewGuid();
        var readiness = CreateReadiness(
            runId,
            CreateBcgRiskRatioReferenceAssessments(runId),
            EffectMeasureType.RiskRatio);

        var result = Assert.Single(CreateSynthesizer().Synthesize(readiness).Results);

        Assert.Equal(QuantitativeSynthesisStatus.Synthesized, result.Status);
        Assert.Equal(QuantitativeSynthesisMethod.FixedEffectInverseVariance, result.Method);
        Assert.NotNull(result.BetweenStudyVariance);
        var estimate = result.BetweenStudyVariance!;
        Assert.Equal(BetweenStudyVarianceEstimator.RestrictedMaximumLikelihood, estimate.Estimator);
        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, estimate.Status);
        Assert.Equal(RestrictedMaximumLikelihoodTauSquaredEstimator.AlgorithmVersion, estimate.AlgorithmVersion);
        Assert.True(estimate.Converged);
        Assert.Null(estimate.FailureReason);
        Assert.Equal(13, estimate.StudyCount);
        Assert.InRange(estimate.IterationCount, 1, 220);
        Assert.NotNull(estimate.TauSquared);
        Assert.Equal(0.3132d, estimate.TauSquared!.Value, 5e-5);
    }

    [Fact]
    public void Estimate_ReturnsZeroTauSquaredForIdenticalEffects()
    {
        var estimate = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate([
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Math.Log(2d), 25d),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Math.Log(2d), 10d),
            CreateContribution(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Math.Log(2d), 4d)
        ]);

        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, estimate.Status);
        Assert.True(estimate.Converged);
        Assert.Equal(0, estimate.IterationCount);
        Assert.Equal(0d, estimate.TauSquared!.Value, 12);
    }

    [Fact]
    public void Estimate_ReturnsZeroTauSquaredAtLowDispersionBoundary()
    {
        var estimate = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate([
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0d, 1d),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0.1d, 1d),
            CreateContribution(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0.2d, 1d)
        ]);

        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, estimate.Status);
        Assert.Equal(0d, estimate.TauSquared!.Value, 12);
        Assert.True(estimate.Converged);
    }

    [Fact]
    public void Estimate_RejectsNonFiniteInputsWithoutFabricatingZero()
    {
        var estimate = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate([
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1d, 1d),
            new QuantitativeSynthesisContribution(
                Guid.NewGuid(),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.NewGuid(),
                Guid.NewGuid(),
                double.NaN,
                1d,
                1d,
                1d,
                0d)
        ]);

        Assert.Equal(BetweenStudyVarianceEstimateStatus.NotEstimated, estimate.Status);
        Assert.False(estimate.Converged);
        Assert.Null(estimate.TauSquared);
        Assert.Equal(BetweenStudyVarianceFailureReason.InvalidInput, estimate.FailureReason);
    }

    [Fact]
    public void Estimate_IsOrderIndependentForBcgReferenceDataset()
    {
        var runId = Guid.NewGuid();
        var contributions = CreateBcgRiskRatioReferenceAssessments(runId)
            .Select(assessment => new QuantitativeSynthesisContribution(
                assessment.EvidenceId,
                assessment.StudyId,
                assessment.EvidenceExtractionId,
                assessment.SourceMaterialId,
                assessment.NormalizedEffect!.Value,
                assessment.Variance!.Value,
                assessment.StandardError!.Value,
                1d / assessment.Variance.Value,
                0d))
            .ToArray();

        var first = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate(contributions);
        var second = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate(contributions.Reverse().ToArray());

        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, first.Status);
        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, second.Status);
        Assert.Equal(first.TauSquared!.Value, second.TauSquared!.Value, 12);
    }

    [Fact]
    public void Estimate_HandlesExtremeFiniteEffectsAndVariances()
    {
        var estimate = RestrictedMaximumLikelihoodTauSquaredEstimator.Estimate([
            CreateContribution(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), -2d, 10000d),
            CreateContribution(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0d, 5d),
            CreateContribution(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 3d, 0.1d)
        ]);

        Assert.Equal(BetweenStudyVarianceEstimateStatus.Estimated, estimate.Status);
        Assert.True(estimate.Converged);
        Assert.NotNull(estimate.TauSquared);
        Assert.True(double.IsFinite(estimate.TauSquared.Value));
        Assert.True(estimate.TauSquared.Value >= 0d);
    }
    private static FixedEffectQuantitativeStatisticalSynthesizer CreateSynthesizer(QuantitativeSynthesisOptions? options = null)
    {
        return new FixedEffectQuantitativeStatisticalSynthesizer(options ?? new QuantitativeSynthesisOptions());
    }


    private static QuantitativeSynthesisContribution CreateContribution(Guid studyId, double analysisScaleEffect, double weight)
    {
        return new QuantitativeSynthesisContribution(
            Guid.NewGuid(),
            studyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            analysisScaleEffect,
            1d / weight,
            Math.Sqrt(1d / weight),
            weight,
            0d);
    }

    private static QuantitativeEvidenceAssessment[] CreateBcgRiskRatioReferenceAssessments(Guid runId)
    {
        // Reference: Viechtbauer metafor dat.bcg example, escalc(measure="RR") followed by rma(yi, vi, method="REML") reports tau^2 = 0.3132.
        var rows = new[]
        {
            (Tpos: 4d, Tneg: 119d, Cpos: 11d, Cneg: 128d),
            (Tpos: 6d, Tneg: 300d, Cpos: 29d, Cneg: 274d),
            (Tpos: 3d, Tneg: 228d, Cpos: 11d, Cneg: 209d),
            (Tpos: 62d, Tneg: 13536d, Cpos: 248d, Cneg: 12619d),
            (Tpos: 33d, Tneg: 5036d, Cpos: 47d, Cneg: 5761d),
            (Tpos: 180d, Tneg: 1361d, Cpos: 372d, Cneg: 1079d),
            (Tpos: 8d, Tneg: 2537d, Cpos: 10d, Cneg: 619d),
            (Tpos: 505d, Tneg: 87886d, Cpos: 499d, Cneg: 87892d),
            (Tpos: 29d, Tneg: 7470d, Cpos: 45d, Cneg: 7232d),
            (Tpos: 17d, Tneg: 1699d, Cpos: 65d, Cneg: 1600d),
            (Tpos: 186d, Tneg: 50448d, Cpos: 141d, Cneg: 27197d),
            (Tpos: 5d, Tneg: 2493d, Cpos: 3d, Cneg: 2338d),
            (Tpos: 27d, Tneg: 16886d, Cpos: 29d, Cneg: 17825d)
        };

        return rows.Select((row, index) =>
        {
            var treatmentTotal = row.Tpos + row.Tneg;
            var controlTotal = row.Cpos + row.Cneg;
            var yi = Math.Log((row.Tpos / treatmentTotal) / (row.Cpos / controlTotal));
            var vi = 1d / row.Tpos - 1d / treatmentTotal + 1d / row.Cpos - 1d / controlTotal;
            return CreateAssessment(
                runId,
                Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
                Guid.Parse($"10000000-0000-0000-0000-{index + 1:000000000000}"),
                yi,
                vi,
                EffectMeasureType.RiskRatio);
        }).ToArray();
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
            effectMeasureType switch { EffectMeasureType.MeanDifference => "mean difference", EffectMeasureType.RiskRatio => "risk ratio", EffectMeasureType.HazardRatio => "hazard ratio", _ => "odds ratio" },
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

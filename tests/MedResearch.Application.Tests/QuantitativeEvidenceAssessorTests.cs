using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using Xunit;

namespace MedResearch.Application.Tests;

public sealed class QuantitativeEvidenceAssessorTests
{
    [Fact]
    public async Task Assess_GroupsCompatibleOddsRatiosWithoutPoolingEffects()
    {
        var runId = Guid.NewGuid();
        var studyA = Guid.NewGuid();
        var studyB = Guid.NewGuid();
        var studyC = Guid.NewGuid();
        var evidenceA = CreateEvidence(runId, studyA, "Depression severity", "Odds Ratio", 1.75m, 1.20m, 2.55m, confidenceLevel: 0.95m);
        var evidenceB = CreateEvidence(runId, studyB, " depression   severity ", "OR", 1.40m, 1.05m, 1.90m, confidenceLevel: 0.95m);
        var evidenceC = CreateEvidence(runId, studyC, "Treatment response", "Odds Ratio", 1.80m, 1.10m, 2.20m, confidenceLevel: 0.95m);
        var corpus = await BuildCorpusAsync(runId, [evidenceA, evidenceB, evidenceC]);

        var readiness = new QuantitativeEvidenceAssessor().Assess(corpus);

        Assert.Equal(3, readiness.EligibleEvidenceCount);
        Assert.Equal(2, readiness.CompatibleGroups.Count);
        var severityGroup = Assert.Single(readiness.CompatibleGroups, group => group.OutcomeGroupKey == "depression severity");
        Assert.Equal(EffectMeasureType.OddsRatio, severityGroup.EffectMeasureType);
        Assert.Equal(2, severityGroup.EvidenceCount);
        Assert.Equal(2, severityGroup.UniqueStudyCount);
        Assert.True(severityGroup.ReadyForFutureMetaAnalysisInput);
        Assert.DoesNotContain(readiness.Assessments, assessment => assessment.NormalizedEffect == (double?)1.575m);
        Assert.All(readiness.Assessments, assessment => Assert.NotNull(assessment.Variance));
    }

    [Fact]
    public async Task Assess_DoesNotGroupDifferentEffectMeasuresForSameOutcome()
    {
        var runId = Guid.NewGuid();
        var oddsRatio = CreateEvidence(runId, Guid.NewGuid(), "Mortality", "Odds Ratio", 1.5m, 1.1m, 2.0m, confidenceLevel: 0.95m);
        var riskRatio = CreateEvidence(runId, Guid.NewGuid(), "Mortality", "Risk Ratio", 1.3m, 1.0m, 1.7m, confidenceLevel: 0.95m);
        var corpus = await BuildCorpusAsync(runId, [oddsRatio, riskRatio]);

        var readiness = new QuantitativeEvidenceAssessor().Assess(corpus);

        Assert.Equal(2, readiness.CompatibleGroups.Count);
        Assert.Contains(readiness.CompatibleGroups, group => group.EffectMeasureType == EffectMeasureType.OddsRatio);
        Assert.Contains(readiness.CompatibleGroups, group => group.EffectMeasureType == EffectMeasureType.RiskRatio);
    }

    [Fact]
    public async Task Assess_DoesNotUsePValueAsEffectMagnitude()
    {
        var runId = Guid.NewGuid();
        var evidence = CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", null, pValue: 0.04m);
        var corpus = await BuildCorpusAsync(runId, [evidence]);

        var assessment = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus).Assessments);

        Assert.Equal(QuantitativeEligibility.Ineligible, assessment.Eligibility);
        Assert.Contains(QuantitativeIneligibilityReason.MissingEffectValue, assessment.ReasonCodes);
        Assert.Null(assessment.NormalizedEffect);
    }

    [Fact]
    public async Task Assess_DoesNotAssumeMissingConfidenceLevelIsNinetyFivePercent()
    {
        var runId = Guid.NewGuid();
        var evidence = CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", 1.75m, 1.20m, 2.55m);
        var corpus = await BuildCorpusAsync(runId, [evidence]);

        var assessment = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus).Assessments);

        Assert.Equal(QuantitativeEligibility.Ineligible, assessment.Eligibility);
        Assert.Contains(QuantitativeIneligibilityReason.MissingConfidenceLevel, assessment.ReasonCodes);
        Assert.Null(assessment.StandardError);
    }

    [Fact]
    public async Task Assess_RejectsInvalidRatioAndNonFiniteBoundaries()
    {
        var runId = Guid.NewGuid();
        var evidence = CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", -1.2m, 0.8m, 1.5m, confidenceLevel: 0.95m);
        var corpus = await BuildCorpusAsync(runId, [evidence]);

        var assessment = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus).Assessments);

        Assert.Equal(QuantitativeEligibility.Ineligible, assessment.Eligibility);
        Assert.Contains(QuantitativeIneligibilityReason.InvalidNumericValue, assessment.ReasonCodes);
        Assert.Null(assessment.NormalizedEffect);
    }

    [Fact]
    public async Task Assess_NormalizesCorrelationOnlyWithSufficientSampleSize()
    {
        var runId = Guid.NewGuid();
        var valid = CreateEvidence(runId, Guid.NewGuid(), "Association", "correlation", 0.28m, sampleSize: 120);
        var invalid = CreateEvidence(runId, Guid.NewGuid(), "Association", "correlation", 0.28m, sampleSize: 3);
        var corpus = await BuildCorpusAsync(runId, [valid, invalid]);

        var readiness = new QuantitativeEvidenceAssessor().Assess(corpus);

        var eligible = Assert.Single(readiness.Assessments, assessment => assessment.Eligibility == QuantitativeEligibility.Eligible);
        Assert.Equal(StatisticOrigin.DerivedFromSampleSize, eligible.StandardErrorOrigin);
        Assert.True(eligible.NormalizedEffect > 0);
        var ineligible = Assert.Single(readiness.Assessments, assessment => assessment.Eligibility == QuantitativeEligibility.Ineligible);
        Assert.Contains(QuantitativeIneligibilityReason.MissingSampleSize, ineligible.ReasonCodes);
    }

    [Fact]
    public async Task Assess_DoesNotCountTwoEvidenceItemsFromOneStudyAsIndependent()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var first = CreateEvidence(runId, studyId, "Depression severity", "Odds Ratio", 1.75m, 1.20m, 2.55m, confidenceLevel: 0.95m);
        var second = CreateEvidence(runId, studyId, "Depression severity", "Odds Ratio", 1.40m, 1.05m, 1.90m, confidenceLevel: 0.95m);
        var corpus = await BuildCorpusAsync(runId, [first, second]);

        var group = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus).CompatibleGroups);

        Assert.Equal(2, group.EvidenceCount);
        Assert.Equal(1, group.UniqueStudyCount);
        Assert.True(group.HasDependentEvidenceFromSameStudy);
        Assert.False(group.ReadyForFutureMetaAnalysisInput);
    }

    [Fact]
    public async Task Assess_IsDeterministicWhenEvidenceInputOrderChanges()
    {
        var runId = Guid.NewGuid();
        var evidence = new[]
        {
            CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", 1.75m, 1.20m, 2.55m, confidenceLevel: 0.95m),
            CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", 1.40m, 1.05m, 1.90m, confidenceLevel: 0.95m),
            CreateEvidence(runId, Guid.NewGuid(), "Treatment response", "Risk Ratio", 1.30m, 1.00m, 1.70m, confidenceLevel: 0.95m)
        };
        var first = new QuantitativeEvidenceAssessor().Assess(await BuildCorpusAsync(runId, evidence));
        var second = new QuantitativeEvidenceAssessor().Assess(await BuildCorpusAsync(runId, evidence.Reverse().ToArray()));

        Assert.Equal(
            first.CompatibleGroups.Select(group => group.GroupKey).ToArray(),
            second.CompatibleGroups.Select(group => group.GroupKey).ToArray());
        Assert.Equal(
            first.Assessments.Select(assessment => assessment.EvidenceId).ToArray(),
            second.Assessments.Select(assessment => assessment.EvidenceId).ToArray());
    }

    [Fact]
    public async Task Assess_SourceTruncationIsLimitationMetadataNotAutomaticIneligibility()
    {
        var runId = Guid.NewGuid();
        var evidence = CreateEvidence(runId, Guid.NewGuid(), "Depression severity", "Odds Ratio", 1.75m, 1.20m, 2.55m, confidenceLevel: 0.95m, sourceWasTruncated: true);
        var corpus = await BuildCorpusAsync(runId, [evidence]);

        var assessment = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus).Assessments);

        Assert.Equal(QuantitativeEligibility.Eligible, assessment.Eligibility);
        Assert.True(assessment.SourceWasTruncated);
        Assert.DoesNotContain(QuantitativeIneligibilityReason.SourceTruncated, assessment.ReasonCodes);
    }

    private static async Task<EvidenceCorpus> BuildCorpusAsync(Guid runId, IReadOnlyCollection<SynthesisEvidenceContext> evidence)
    {
        var studies = evidence
            .Select(item => item.StudyId)
            .Distinct()
            .Select((studyId, index) => new SynthesisStudySnapshot(
                studyId,
                $"Study {index + 1}",
                (12345000 + index).ToString(),
                null,
                $"10.1000/{index + 1}",
                "Journal",
                new DateOnly(2026, 1, index + 1),
                ["Journal Article"],
                ["Ada Lovelace"],
                "PubMed",
                DateTimeOffset.UtcNow.AddMinutes(index)))
            .ToArray();
        var sourceByExtraction = evidence.ToDictionary(item => item.EvidenceExtractionId, item => Guid.NewGuid());
        var extractions = evidence
            .Select(item => new SynthesisExtractionSnapshot(
                item.EvidenceExtractionId,
                runId,
                item.StudyId,
                EvidenceExtractionStatus.Completed,
                null,
                item.SourceScope,
                sourceByExtraction[item.EvidenceExtractionId],
                1,
                true))
            .ToArray();
        var sourceMaterials = evidence
            .Select(item => new SynthesisSourceMaterialSnapshot(
                sourceByExtraction[item.EvidenceExtractionId],
                item.StudyId,
                item.SourceScope == EvidenceSourceScope.StructuredFullText ? SourceMaterialType.StructuredFullText : SourceMaterialType.Abstract,
                "TestSource",
                null,
                SourceMaterial.ComputeContentHash(item.SupportingText),
                1,
                item.SupportingText.Contains("TRUNCATED", StringComparison.Ordinal),
                true))
            .ToArray();
        var snapshot = new SynthesisCorpusSnapshot(
            runId,
            Guid.NewGuid(),
            "Does the intervention improve outcomes?",
            null,
            studies,
            evidence,
            [],
            [new SynthesisSearchSnapshot(Guid.NewGuid(), runId, "PubMed", "test", DateTimeOffset.UtcNow, studies.Length, studies.Length, 0)],
            extractions,
            sourceMaterials);

        return await new EvidenceCorpusBuilder(new StaticStore(snapshot)).BuildAsync(runId, CancellationToken.None);
    }

    private static SynthesisEvidenceContext CreateEvidence(
        Guid runId,
        Guid studyId,
        string outcome,
        string? effectMeasure,
        decimal? effectValue,
        decimal? lower = null,
        decimal? upper = null,
        decimal? confidenceLevel = null,
        decimal? pValue = null,
        int? sampleSize = 120,
        bool sourceWasTruncated = false)
    {
        return new SynthesisEvidenceContext(
            Guid.NewGuid(),
            runId,
            studyId,
            Guid.NewGuid(),
            outcome,
            "Reported quantitative result.",
            sourceWasTruncated ? "Reported quantitative result. TRUNCATED" : "Reported quantitative result.",
            EvidenceDirection.Positive,
            EvidenceSourceScope.Abstract,
            DateTimeOffset.UtcNow,
            "adults with depressive symptoms",
            "intervention",
            "placebo",
            "randomized controlled trial",
            sampleSize,
            effectMeasure,
            effectValue,
            lower,
            upper,
            pValue,
            confidenceLevel,
            null);
    }

    private sealed class StaticStore : ISynthesisCorpusStore
    {
        private readonly SynthesisCorpusSnapshot _snapshot;

        public StaticStore(SynthesisCorpusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }
    }
}
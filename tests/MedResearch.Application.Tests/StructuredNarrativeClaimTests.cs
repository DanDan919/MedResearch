using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace MedResearch.Application.Tests;

public sealed class StructuredNarrativeClaimTests
{
    [Fact]
    public void ValidStructuredClaimRendersBackendScopeAndDropsAllModelSections()
    {
        var evidence = Evidence();
        var result = Validate(Context(evidence), Draft(evidence) with { Claims = [Proposal(evidence)], Conclusion = "Treatment cures every patient; OR=99." });
        var claim = Assert.Single(result.Claims);
        Assert.Equal(ResearchClaimKind.QualitativeEffect, claim.Semantics!.Kind);
        Assert.Contains("mortality", claim.Text);
        Assert.Contains("healthy adults", claim.Text);
        Assert.Contains("6 weeks", claim.Text);
        Assert.DoesNotContain("99", result.Conclusion);
        Assert.DoesNotContain("cures", result.Conclusion);
    }

    [Theory]
    [InlineData("outcome", "ClaimOutcomeMismatch")]
    [InlineData("population", "ClaimPopulationMismatch")]
    [InlineData("intervention", "ClaimInterventionMismatch")]
    [InlineData("comparator", "ClaimComparatorMismatch")]
    [InlineData("timepoint", "ClaimTimepointMismatch")]
    [InlineData("direction", "InvalidDirection")]
    [InlineData("kind", "SynthesisContractViolation")]
    public void StructuredMismatchIsRejectedWithTypedIssue(string field, string code)
    {
        var item = Evidence();
        var proposal = Proposal(item);
        proposal = field switch
        {
            "outcome" => proposal with { Outcome = "infection" },
            "population" => proposal with { Population = "all adults" },
            "intervention" => proposal with { ExposureOrIntervention = "Drug B" },
            "comparator" => proposal with { Comparator = "active therapy" },
            "timepoint" => proposal with { Timepoint = "12 weeks" },
            "direction" => proposal with { Direction = "Negative" },
            _ => proposal with { Kind = "999" }
        };
        var failure = Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), Draft(item) with { Claims = [proposal] }));
        Assert.Equal(code, Assert.Single(failure.Issues).Code);
    }

    [Theory]
    [InlineData(EvidenceDirection.Negative)]
    [InlineData(EvidenceDirection.NoClearEffect)]
    [InlineData(EvidenceDirection.NotReported)]
    public void DifferingDirectionsCannotBecomeUniformButCanBecomeMixed(EvidenceDirection otherDirection)
    {
        var a = Evidence();
        var b = a with { EvidenceId = Guid.NewGuid(), Direction = otherDirection };
        var context = Context(a, b);
        var proposal = Proposal(a) with { EvidenceIds = [a.EvidenceId.ToString(), b.EvidenceId.ToString()] };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, Draft(a) with { Claims = [proposal] }));
        var mixed = Validate(context, Draft(a) with { Claims = [proposal with { Kind = "MixedEvidence", Direction = "Mixed" }] });
        Assert.Contains("mixed or differing", Assert.Single(mixed.Claims).Text);
        var subset = Validate(context, Draft(a) with { Claims = [Proposal(a)] });
        Assert.Single(Assert.Single(subset.Claims).EvidenceIds);
    }

    [Fact]
    public void MissingScopeIsNotWildcardAndNoClearEffectIsNotAbsence()
    {
        var item = Evidence() with { Population = null, Direction = EvidenceDirection.NoClearEffect };
        var proposal = Proposal(item) with { Direction = "NoClearEffect" };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), Draft(item) with { Claims = [proposal with { Population = "adults" }] }));
        Assert.Contains("not proof of no effect", Assert.Single(Validate(Context(item), Draft(item) with { Claims = [proposal] }).Claims).Text);
        var empty = Context();
        var insufficiency = new ResearchReportClaimDraft("Conclusion", "NotApplicable", null, [], Kind: "InsufficientEvidence");
        var draft = new ResearchReportDraft("InsufficientEvidence", "NoValidatedEvidence", null, null, null, null, null, "InsufficientEvidence", [insufficiency]);
        Assert.Contains("not evidence of no effect", Assert.Single(Validate(empty, draft).Claims).Text);
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(empty, draft with { Claims = [insufficiency with { Kind = "QualitativeEffect", Direction = "NoClearEffect" }] }));
    }

    [Fact]
    public void ReportedNumbersAreSelectedFromVerifiedTupleAndNotModelFields()
    {
        var item = GroundedEvidenceFixture.Create(Evidence() with { EffectMeasure = "odds ratio", PValue = null }, Guid.NewGuid());
        var proposal = Proposal(item) with { Kind = "ReportedStudyResult", Direction = "NotApplicable", NumericEvidenceId = item.EvidenceId.ToString(), Statistic = "StudyConfidenceInterval" };
        var claim = Assert.Single(Validate(Context(item), Draft(item) with { Claims = [proposal] }).Claims);
        Assert.Equal(0.73m, claim.Semantics!.Numeric!.StudyValue);
        Assert.Equal(0.55m, claim.Semantics.Numeric.StudyLower);
        Assert.Contains("95% confidence interval", claim.Text);
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), Draft(item) with { Claims = [proposal with { NumericEvidenceId = Guid.NewGuid().ToString() }] }));
        var field = JsonSerializer.Deserialize<JsonElement>("0.63");
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), Draft(item) with { Claims = [proposal with { UnrecognizedFields = new() { ["estimate"] = field } }] }));
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item with { NumericGrounding = [] }), Draft(item) with { Claims = [proposal] }));
        var other = item with { EvidenceId = Guid.NewGuid() };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item, other), Draft(item) with { Claims = [proposal with { EvidenceIds = [item.EvidenceId.ToString(), other.EvidenceId.ToString()] }] }));
    }

    [Theory]
    [InlineData("FixedEffectWald", 0.73, "confidence interval")]
    [InlineData("CochransQ", 2.0, "Cochran's Q")]
    [InlineData("ISquared", 0.42, "proportion")]
    [InlineData("TauSquared", 0.01, "analysis-scale variance")]
    public void PooledSelectorsUseExactPersistedArtifactValues(string statistic, double value, string label)
    {
        var item = Evidence();
        var artifact = Artifact(item);
        var context = Context(item) with { QuantitativeArtifacts = [artifact] };
        var proposal = Proposal(item) with { Kind = "QuantitativeSynthesis", Direction = "NotApplicable", QuantitativeArtifactId = artifact.ArtifactId.ToString(), Statistic = statistic };
        var claim = Assert.Single(Validate(context, Draft(item) with { Claims = [proposal] }).Claims);
        Assert.Equal(value, claim.Semantics!.Numeric!.ArtifactValue);
        Assert.Equal(artifact.ArtifactId, claim.Semantics.QuantitativeArtifactId);
        Assert.Contains(label, claim.Text);
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, Draft(item) with { Claims = [proposal with { QuantitativeArtifactId = Guid.NewGuid().ToString() }] }));
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, Draft(item) with { Claims = [proposal with { UnrecognizedFields = new() { ["pooledEstimate"] = JsonSerializer.Deserialize<JsonElement>("0.81") } }] }));
    }

    [Fact]
    public void UnknownEvidenceForeignArtifactAndDuplicateSemanticsFailClosed()
    {
        var item = Evidence();
        var draft = Draft(item) with { Claims = [Proposal(item)] };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), draft with { Claims = [Proposal(item) with { EvidenceIds = [Guid.NewGuid().ToString()] }] }));
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item with { ResearchRunId = Guid.NewGuid() }) with { ResearchRunId = item.ResearchRunId }, draft));
        var artifact = Artifact(item);
        var foreign = artifact.Result with { ResearchRunId = Guid.NewGuid() };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item) with { QuantitativeArtifacts = [artifact with { Result = foreign, SnapshotFingerprint = QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(foreign) }] }, draft));
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), draft with { Claims = [Proposal(item), Proposal(item) with { Type = "Finding" }] }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepairUsesSameStructuredSupportAndNeverAttemptsThirdCandidate(bool repairSucceeds)
    {
        var item = Evidence();
        var invalid = Draft(item) with { Claims = [Proposal(item) with { Population = "all adults" }] };
        var valid = Draft(item) with { Claims = [Proposal(item)] };
        var client = new SequenceClient(invalid, repairSucceeds ? valid : invalid);
        var synthesizer = new ResearchSynthesizer(client, new(new()), NullLogger<ResearchSynthesizer>.Instance);
        if (repairSucceeds) Assert.Single((await synthesizer.SynthesizeAsync(Context(item), CancellationToken.None)).Claims);
        else await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() => synthesizer.SynthesizeAsync(Context(item), CancellationToken.None));
        Assert.Equal(2, client.Requests.Count);
        Assert.StartsWith(client.Requests[0].UserPrompt, client.Requests[1].UserPrompt);
        Assert.Contains("ClaimPopulationMismatch", client.Requests[1].UserPrompt);
        Assert.Equal(client.Requests[0].OutputSchema, client.Requests[1].OutputSchema);
    }

    [Theory]
    [InlineData("RandomEffectsWald", 0.55, 0.96, "confidence interval")]
    [InlineData("RandomEffectsHksj", 0.40, 1.20, "confidence interval")]
    [InlineData("RandomEffectsPredictionInterval", 0.30, 1.40, "prediction interval")]
    public void RandomIntervalsKeepSharedPointEstimateButDistinctIntervalSemantics(string statistic, double lower, double upper, string label)
    {
        var item = Evidence();
        var artifact = Artifact(item);
        var hksj = new QuantitativeHksjInferenceResult(QuantitativeSynthesisStatus.Synthesized, QuantitativeConfidenceIntervalMethod.HartungKnappSidikJonkman,
            "hksj-fixture", 0.95m, 3, 2, 1.2, 4.3, -0.31, 0.02, 0.14, -0.91, 0.18, 0.73, 0.40, 1.20, []);
        var pi = new QuantitativePredictionIntervalResult(QuantitativeSynthesisStatus.Synthesized, QuantitativePredictionIntervalMethod.CochraneRandomEffectsStudentT,
            "pi-fixture", 0.95m, 3, 2, 0.01, 0.02, 0.14, 0.03, 0.17, 4.3, -0.31, -1.20, 0.34, 0.73, 0.30, 1.40, []);
        var random = new QuantitativeRandomEffectsSynthesisResult(QuantitativeSynthesisStatus.Synthesized, QuantitativeSynthesisMethod.RandomEffectsInverseVariance,
            "random-fixture", QuantitativeConfidenceIntervalMethod.WaldStandardNormal, 0.95m, 0.01, BetweenStudyVarianceEstimator.RestrictedMaximumLikelihood,
            "tau-fixture", 3, -0.31, 0.02, 0.14, -0.60, -0.04, 0.73, 0.55, 0.96, hksj, pi, artifact.Result.Contributions, []);
        var result = artifact.Result with { RandomEffects = random };
        artifact = artifact with { Result = result, SnapshotFingerprint = QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result) };
        var draft = Proposal(item) with { Kind = "QuantitativeSynthesis", Direction = "NotApplicable", QuantitativeArtifactId = artifact.ArtifactId.ToString(), Statistic = statistic };
        var claim = Assert.Single(Validate(Context(item) with { QuantitativeArtifacts = [artifact] }, Draft(item) with { Claims = [draft] }).Claims);
        Assert.Equal(0.73, claim.Semantics!.Numeric!.ArtifactValue);
        Assert.Equal(lower, claim.Semantics.Numeric.ArtifactLower);
        Assert.Equal(upper, claim.Semantics.Numeric.ArtifactUpper);
        Assert.Contains(label, claim.Text);
        if (statistic == "RandomEffectsPredictionInterval") Assert.DoesNotContain("confidence interval", claim.Text);
    }

    [Fact]
    public void ArtifactSupportSetAndMissingIntervalsCannotBeSubstituted()
    {
        var item = Evidence();
        var other = item with { EvidenceId = Guid.NewGuid(), StudyId = Guid.NewGuid() };
        var artifact = Artifact(item);
        var context = Context(item, other) with { QuantitativeArtifacts = [artifact] };
        var proposal = Proposal(item) with { Kind = "QuantitativeSynthesis", Direction = "NotApplicable", QuantitativeArtifactId = artifact.ArtifactId.ToString(), Statistic = "FixedEffectWald" };
        foreach (var ids in new[] { new[] { other.EvidenceId.ToString() }, new[] { item.EvidenceId.ToString(), other.EvidenceId.ToString() } })
            Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, Draft(item) with { Claims = [proposal with { EvidenceIds = ids }] }));
        foreach (var statistic in new[] { "RandomEffectsHksj", "RandomEffectsPredictionInterval", "StudyEffect" })
            Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, Draft(item) with { Claims = [proposal with { Statistic = statistic }] }));
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("role")]
    [InlineData("statistic")]
    public void UndefinedNumericEnumValuesAreNotNamedCategories(string field)
    {
        var item = Evidence();
        var proposal = Proposal(item);
        proposal = field switch { "direction" => proposal with { Direction = "999" }, "role" => proposal with { Type = "999" }, _ => proposal with { Kind = "ReportedStudyResult", Direction = "NotApplicable", NumericEvidenceId = item.EvidenceId.ToString(), Statistic = "999" } };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(Context(item), Draft(item) with { Claims = [proposal] }));
    }

    internal static ResearchReportClaimDraft Proposal(SynthesisEvidenceContext item) => new("Conclusion", "Positive", null, [item.EvidenceId.ToString()],
        Kind: "QualitativeEffect", Outcome: item.Outcome, Population: item.Population, ExposureOrIntervention: item.ExposureOrIntervention,
        Comparator: item.Comparator, Timepoint: item.Timepoint);

    internal static QuantitativeSynthesisArtifactReadModel Artifact(SynthesisEvidenceContext item)
    {
        var result = new QuantitativeSynthesisResult(item.ResearchRunId, "exact-fixture-group", "mortality", "healthy adults", "placebo", "trial", EffectMeasureType.OddsRatio,
            QuantitativeSynthesisStatus.Synthesized, QuantitativeSynthesisMethod.FixedEffectInverseVariance, "fixture-v1", 0.95m, 1, 1,
            Math.Log(0.73), 0.02, Math.Sqrt(0.02), Math.Log(0.55), Math.Log(0.96), 0.73, 0.55, 0.96,
            new(2, 1, 0.42, 2, "fixture-q"), new(0.01, BetweenStudyVarianceEstimator.RestrictedMaximumLikelihood, BetweenStudyVarianceEstimateStatus.Estimated, "fixture-tau", 2, true, 1, null), null,
            [new(item.EvidenceId, item.StudyId, item.EvidenceExtractionId, Guid.NewGuid(), Math.Log(0.73), 0.02, Math.Sqrt(0.02), 50, 1)], []);
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result), result);
    }

    private sealed class SequenceClient(params ResearchReportDraft[] drafts) : IStructuredLlmClient
    {
        private readonly Queue<ResearchReportDraft> _drafts = new(drafts);
        public List<StructuredLlmRequest> Requests { get; } = [];
        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new StructuredGenerationResult<T>((T)(object)_drafts.Dequeue(), new("fake", "fake", null, DateTimeOffset.UtcNow)));
        }
    }
    [Theory]
    [InlineData("direction", "Treatment reduces mortality despite reported harm.")]
    [InlineData("outcome", "Treatment reduces infection instead of mortality.")]
    [InlineData("population", "Treatment benefits patients with dementia.")]
    [InlineData("intervention", "Drug B benefits mortality.")]
    [InlineData("comparator", "Drug A improves mortality compared with active therapy.")]
    [InlineData("timepoint", "Treatment benefits mortality at 12 weeks.")]
    [InlineData("number", "OR was 0.63 (95% CI 0.55-0.96).")]
    [InlineData("statistic", "P-value was 0.73 and odds ratio was 0.03.")]
    [InlineData("mixed", "All cited studies consistently demonstrate benefit.")]
    [InlineData("insufficient", "Evidence definitively demonstrates benefit.")]
    public void RejectsUnsupportedAuthoritativeProse(string attack, string text)
    {
        var evidence = Evidence() with { Direction = attack == "direction" ? EvidenceDirection.Negative : EvidenceDirection.Positive };
        var context = Context(evidence);
        if (attack == "mixed")
            context = Context(evidence, evidence with { EvidenceId = Guid.NewGuid(), Direction = EvidenceDirection.NoClearEffect });
        var draft = Draft(evidence) with
        {
            ReportStatus = attack == "insufficient" ? "InsufficientEvidence" : "Completed",
            InsufficientEvidenceReason = attack == "insufficient" ? "EvidenceTooSparse" : null,
            Claims = [new ResearchReportClaimDraft("Conclusion", attack == "direction" ? "Negative" : "Positive", text,
                context.Studies.SelectMany(x => x.Evidence).Select(x => x.EvidenceId.ToString()).ToArray())]
        };
        Assert.Throws<ResearchSynthesisValidationException>(() => Validate(context, draft));
    }

    internal static SynthesisEvidenceContext Evidence() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "mortality", "untrusted summary", "source quotation", EvidenceDirection.Positive, EvidenceSourceScope.Abstract,
        DateTimeOffset.UtcNow, "healthy adults", "Drug A", "placebo", "controlled trial", 247, "OR", 0.73m, 0.55m, 0.96m, 0.03m, 0.95m,
        Timepoint: "6 weeks");

    internal static SynthesisContext Context(params SynthesisEvidenceContext[] evidence)
    {
        var studies = evidence.GroupBy(x => x.StudyId).Select(g => new SynthesisStudyContext(g.Key, "Study", null, null, null,
            null, null, [], [], "PubMed", null, g.ToArray())).ToArray();
        return new SynthesisContext(evidence.FirstOrDefault()?.ResearchRunId ?? Guid.NewGuid(), Guid.NewGuid(), "Question", null,
            new SynthesisCorpusStatistics(studies.Length, studies.Length, 0, evidence.Length, studies.Length, evidence.Length, 1, 0, 0, 0, studies.Length, 0),
            new SynthesisSourceCoverage(["PubMed"], true, false, false, false, 1), studies, [], ["Abstract source coverage."]);
    }

    internal static ResearchReportDraft Draft(SynthesisEvidenceContext evidence) => new("Completed", null, "summary", "evidence", "conflict",
        "limitations", "conclusion", "Limited", [new ResearchReportClaimDraft("Conclusion", "Positive", "Evidence-supported claim.", [evidence.EvidenceId.ToString()])]);

    internal static ResearchSynthesisResult Validate(SynthesisContext context, ResearchReportDraft draft) =>
        new ResearchReportDraftValidator(new SynthesisOptions()).Validate(context, draft, "fake", "fake", DateTimeOffset.UtcNow);
}

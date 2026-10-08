using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Application.Research.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using MedResearch.Application.Research.Evaluation;

namespace MedResearch.Application.Tests;

public sealed class ScientificTrustBoundaryCorrectionTests
{
    [Theory]
    [InlineData("Mortality OR 0.73 (95% CI 0.55 to 0.96), whereas infection OR 1.42 (95% CI 1.10 to 1.85).", "Mortality", "OR", "0.73", "1.10", "1.85", NumericGroundingField.ConfidenceInterval)]
    [InlineData("Mortality MD = -0.73 (95% CI -0.96 to -0.55).", "Mortality", "MD", "0.73", "-0.96", "-0.55", NumericGroundingField.EffectEstimate)]
    [InlineData("Mortality or infection had HR = 0.73 (95% CI 0.55 to 0.96).", "Mortality", "OR", "0.73", "0.55", "0.96", NumericGroundingField.EffectMeasure)]
    [InlineData("Mortality was 0.55 or 0.73 (95% CI 0.55 to 0.96).", "Mortality", "OR", "0.73", "0.55", "0.96", NumericGroundingField.EffectMeasure)]
    [InlineData("Mortality OR 0.73. Infection OR 1.42 (95% CI 1.10 to 1.85).", "Mortality", "OR", "1.42", "1.10", "1.85", NumericGroundingField.EffectEstimate)]
    public void Verifier_DoesNotVerifyCrossBoundOrMislabelledStatistics(
        string source, string outcome, string measure, string effect, string lower, string upper, NumericGroundingField field)
    {
        var draft = Finding(source, outcome, measure, Decimal(effect), Decimal(lower), Decimal(upper));
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, field).Status);
    }

    [Fact]
    public void Verifier_DoesNotBindHospitalCountToParticipantRole()
    {
        var draft = Finding("247 participants were randomized across 63 hospitals. Mortality OR 0.73.") with { SampleSize = 63 };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.SampleSize).Status);
    }

    [Fact]
    public void Verifier_DoesNotBorrowConfidenceLevelFromAnotherResult()
    {
        var draft = Finding("Mortality OR 0.73 (90% CI 0.55 to 0.96). Infection OR 1.42 (95% CI 1.10 to 1.85).") with { ConfidenceLevel = 0.95m };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.ConfidenceLevel).Status);
    }

    [Fact]
    public void Verifier_DoesNotBorrowStandardErrorFromAnotherResult()
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96). Infection OR 1.42, SE = 0.21.") with { ReportedStandardError = 0.21m };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.StandardError).Status);
    }

    [Fact]
    public void Resolver_RecognizesOverlappingMatches()
    {
        Assert.Equal(NumericGroundingStatus.Ambiguous, new SourceAnchorResolver().Resolve(Guid.NewGuid(), "aaaa", "aaa").Status);
    }

    [Fact]
    public void Resolver_DerivesLexicalCaseFromSourceNotCandidate()
    {
        const string source = "Mortality was 0.55 or 0.73 (95% CI 0.55 to 0.96).";
        var anchor = new SourceAnchorResolver().Resolve(Guid.NewGuid(), source, source.Replace(" or ", " OR ")).Anchor!;
        Assert.Equal(source, anchor.LexicalText);
        Assert.NotEqual(NumericGroundingStatus.Verified, new SemanticNumericGroundingVerifier()
            .Verify(Finding(source), anchor).Facts.Single(fact => fact.Field == NumericGroundingField.EffectMeasure).Status);
    }

    [Fact]
    public void Verifier_LegacyCaseFoldedAnchorCannotProveBareOr()
    {
        const string source = "Mortality OR 0.73 (95% CI 0.55 to 0.96).";
        var anchor = new SourceAnchorResolver().Resolve(Guid.NewGuid(), source, source).Anchor!;
        Assert.Equal(NumericGroundingStatus.Verified, new SemanticNumericGroundingVerifier()
            .Verify(Finding(source), anchor).Facts.Single(fact => fact.Field == NumericGroundingField.EffectMeasure).Status);
        Assert.NotEqual(NumericGroundingStatus.Verified, new SemanticNumericGroundingVerifier()
            .Verify(Finding(source), anchor with { LexicalText = null }).Facts.Single(fact => fact.Field == NumericGroundingField.EffectMeasure).Status);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("−")]
    [InlineData("–")]
    public void Verifier_PreservesSignedEffect(string minus)
    {
        var draft = Finding($"Mortality MD = {minus}0.73 (95% CI -0.96 to -0.55).", measure: "MD", effect: -0.73m, lower: -0.96m, upper: -0.55m);
        Assert.Equal(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.EffectEstimate).Status);
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft with { EffectValue = 0.73m }, NumericGroundingField.EffectEstimate).Status);
    }

    [Fact]
    public void Verifier_DoesNotBorrowPValue()
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96). Infection OR 1.42, p < 0.03.") with { PValue = 0.03m, PValueOperator = "<" };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.PValue).Status);
    }

    [Fact]
    public void Verifier_MultiplePlausibleTuplesAreAmbiguous()
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96). Mortality OR 0.73 (90% CI 0.52 to 0.98).");
        Assert.Equal(NumericGroundingStatus.Ambiguous, Fact(draft, NumericGroundingField.EffectEstimate).Status);
    }

    [Fact]
    public void Verifier_UnlabelledMultipleResultsAreNotGuessed()
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96), infection OR 1.42 (95% CI 1.10 to 1.85).");
        Assert.Equal(NumericGroundingStatus.Ambiguous, Fact(draft, NumericGroundingField.EffectEstimate).Status);
    }

    [Fact]
    public void Verifier_DoesNotAttachUnlabelledForeignCiAfterFreeText()
    {
        var draft = Finding("Mortality OR 0.73, infection had 95% CI 1.10 to 1.85.", lower: 1.10m, upper: 1.85m);
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.ConfidenceInterval).Status);
    }

    [Fact]
    public void Verifier_UnknownSampleSizeScopeIsAmbiguous()
    {
        var draft = Finding("120 participants. Mortality OR 0.73.") with { SampleSize = 120 };
        Assert.Equal(NumericGroundingStatus.Ambiguous, Fact(draft, NumericGroundingField.SampleSize).Status);
    }

    [Fact]
    public void Verifier_DoesNotReverseTreatmentAndComparatorRoles()
    {
        var draft = Finding("In adults, drug A versus placebo at 12 weeks: Mortality OR 0.73 (95% CI 0.55 to 0.96).")
            with { ExposureOrIntervention = "placebo", Comparator = "drug A", Timepoint = "12 weeks" };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.ExposureOrIntervention).Status);
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.Comparator).Status);
    }

    [Fact]
    public void Verifier_DoesNotTreatTreatmentDurationAsOutcomeTimepoint()
    {
        var draft = Finding("In adults, drug A versus placebo for 12 weeks: Mortality OR 0.73 (95% CI 0.55 to 0.96).") with { Timepoint = "12 weeks" };
        Assert.NotEqual(NumericGroundingStatus.Verified, Fact(draft, NumericGroundingField.Timepoint).Status);
    }

    [Fact]
    public void Verifier_DoesNotSelectOneOfSeveralSampleScopes()
    {
        var draft = Finding("247 participants were enrolled. 120 participants were analyzed. Mortality OR 0.73.") with { SampleSize = 120 };
        Assert.Equal(NumericGroundingStatus.Ambiguous, Fact(draft, NumericGroundingField.SampleSize).Status);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    public void Validator_RejectsUndefinedDirection(string direction)
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96).") with { Direction = direction };
        Assert.Throws<EvidenceExtractionValidationException>(() => Accept(draft));
    }

    [Fact]
    public async Task Assessor_DifferentInterventionsCannotProduceOneReadyGroup()
    {
        var corpus = await Corpus("drug A", "drug B");
        var readiness = new QuantitativeEvidenceAssessor().Assess(corpus);
        Assert.DoesNotContain(readiness.CompatibleGroups, group => group.ReadyForFutureMetaAnalysisInput);
        Assert.Equal(2, readiness.EligibleEvidenceCount);
        Assert.Equal(2, readiness.CompatibleGroups.Count);
    }

    [Fact]
    public async Task Assessor_SameExplicitEstimandRemainsReady()
    {
        var corpus = await Corpus("drug A", " drug   A ");
        var readiness = new QuantitativeEvidenceAssessor().Assess(corpus);
        Assert.Equal(2, readiness.EligibleEvidenceCount);
        Assert.True(Assert.Single(readiness.CompatibleGroups).ReadyForFutureMetaAnalysisInput);
        var reversed = new QuantitativeEvidenceAssessor().Assess(corpus with { Evidence = corpus.Evidence.Reverse().ToArray() });
        Assert.Equal(readiness.CompatibleGroups.First().GroupKey, reversed.CompatibleGroups.First().GroupKey);
    }

    [Theory]
    [InlineData("drug A", "6 weeks", "placebo", "drug A", "12 weeks", "placebo")]
    [InlineData("drug A", "12 weeks", "placebo", "drug A", null, "placebo")]
    [InlineData("drug A", "12 weeks", "placebo", null, "12 weeks", "placebo")]
    [InlineData("drug A", "12 weeks", "placebo", "drug A", "12 weeks", "standard care")]
    public async Task Assessor_IncompleteOrDifferentEstimandsCannotPool(string? firstDrug, string? firstTime, string firstComparator, string? secondDrug, string? secondTime, string secondComparator)
    {
        var corpus = await CorpusFor((firstDrug, firstTime, firstComparator), (secondDrug, secondTime, secondComparator));
        Assert.DoesNotContain(new QuantitativeEvidenceAssessor().Assess(corpus).CompatibleGroups, group => group.ReadyForFutureMetaAnalysisInput);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("confidence")]
    public async Task Assessor_RequiresAllDerivationProof(string corruption)
    {
        var corpus = await Corpus("drug A");
        var original = Assert.Single(corpus.Evidence);
        var evidence = original with { NumericGrounding = corruption switch
        {
            "null" => null,
            "empty" => [],
            _ => original.NumericGrounding!.Select(fact => fact.Field == NumericGroundingField.ConfidenceLevel ? fact with { Status = NumericGroundingStatus.Unsupported } : fact).ToArray()
        } };
        var result = Assert.Single(new QuantitativeEvidenceAssessor().Assess(corpus with { Evidence = [evidence] }).Assessments);
        Assert.Equal(QuantitativeEligibility.Ineligible, result.Eligibility);
        Assert.Null(result.StandardError);
        Assert.Null(result.Variance);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("study")]
    [InlineData("status")]
    public async Task Assessor_DirectSnapshotCannotBypassExtractionLineage(string corruption)
    {
        var corpus = await Corpus("drug A");
        var extraction = Assert.Single(corpus.Extractions);
        extraction = corruption switch
        {
            "run" => extraction with { ResearchRunId = Guid.NewGuid() },
            "study" => extraction with { StudyId = Guid.NewGuid() },
            _ => extraction with { Status = EvidenceExtractionStatus.Skipped }
        };
        Assert.Equal(0, new QuantitativeEvidenceAssessor().Assess(corpus with { Extractions = [extraction] }).EligibleEvidenceCount);
    }

    [Theory]
    [InlineData("missing-anchor")]
    [InlineData("unknown-version")]
    [InlineData("offsets")]
    [InlineData("non-member")]
    [InlineData("lexical-case")]
    public async Task Corpus_RejectsCorruptSourceProof(string corruption)
    {
        var corpus = await Corpus("drug A");
        var original = Assert.Single(corpus.Evidence);
        var facts = original.NumericGrounding!.Select(fact => fact with { Anchor = corruption switch
        {
            "missing-anchor" => null,
            "unknown-version" => fact.Anchor! with { NormalizationVersion = "source-text-v999" },
            "offsets" => fact.Anchor! with { StartOffset = 100000, EndOffset = 100000 + fact.Anchor!.Text.Length },
            "lexical-case" => fact.Anchor! with { LexicalText = fact.Anchor!.Text },
            _ => fact.Anchor
        } }).ToArray();
        var sources = corpus.SourceMaterials;
        if (corruption == "non-member")
        {
            const string wrong = "Different source contents.";
            sources = sources.Select(source => source with { Content = wrong, ContentHash = SourceMaterial.ComputeContentHash(wrong) }).ToArray();
        }
        var snapshot = corpus.Snapshot with { Evidence = [original with { NumericGrounding = facts }], SourceMaterials = sources };
        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() => new EvidenceCorpusBuilder(new Store(snapshot)).BuildAsync(corpus.ResearchRunId, CancellationToken.None));
        Assert.Equal(0, new QuantitativeEvidenceAssessor().Assess(corpus with { Evidence = snapshot.Evidence, SourceMaterials = sources }).EligibleEvidenceCount);
    }

    [Theory]
    [InlineData("Qualitative improvement reported.")]
    [InlineData("Mortality OR 0.73 (95% CI 0.55 to 0.96).")]
    public void Validator_PreservesQualitativeEvidenceAndGroundedNumericalFields(string summary)
    {
        var accepted = Accept(Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96).") with { ResultSummary = summary });
        Assert.Equal(0.73m, accepted.EffectValue);
        Assert.Equal(0.55m, accepted.ConfidenceIntervalLower);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synthesizer_ActualLlmRequestExcludesRejectedNumbers(bool rejectEffect)
    {
        var draft = Finding("Mortality OR 0.73 (95% CI 0.55 to 0.96).") with
        {
            EffectValue = rejectEffect ? 999.123m : 0.73m,
            ConfidenceIntervalLower = 0.1234m,
            ConfidenceIntervalUpper = 0.2345m,
            ResultSummary = "OR 999.123 with CI 0.1234 to 0.2345."
        };
        var evidence = Evidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Accept(draft));
        var llm = new CapturingClient();
        var synthesizer = new ResearchSynthesizer(llm, new ResearchReportDraftValidator(new SynthesisOptions()), NullLogger<ResearchSynthesizer>.Instance);
        await Assert.ThrowsAsync<PromptCapturedException>(() => synthesizer.SynthesizeAsync(Synthesis(evidence), CancellationToken.None));
        Assert.DoesNotContain("999.123", llm.Request!.UserPrompt);
        Assert.DoesNotContain("0.1234", llm.Request.UserPrompt);
        Assert.DoesNotContain("0.2345", llm.Request.UserPrompt);
        if (!rejectEffect) Assert.Contains("EffectValue: 0.73", llm.Request.UserPrompt);
    }

    [Fact]
    public void EvaluationPrompt_DoesNotReintroduceRawExtractionSummary()
    {
        const string source = "Mortality OR 0.73 (95% CI 0.55 to 0.96).";
        var evidence = new EvaluationEvidenceContext(Guid.NewGuid(), "Mortality", "CI 999.123 to 999.456", source, EvidenceDirection.Positive,
            null, null, null, null, null, "OR", 0.73m, 0.55m, 0.96m, null, true);
        var context = new EvaluationStudyContext(Guid.NewGuid(), Guid.NewGuid(), "Question", null, Guid.NewGuid(), Guid.NewGuid(), "Fixture",
            source, SourceMaterial.ComputeContentHash(source), false, ["Abstract"], "Study", source, null, null, null, null, null, [], [], "Fixture",
            EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, EvidenceExtractionPrompt.Version, [evidence]);
        var prompt = EvidenceEvaluationPrompt.Create(context, new(EvidenceSourceScope.Abstract, 1, false, true, true, false, false, StudyDesignClassification.Unknown, []));
        Assert.DoesNotContain("999.123", prompt.UserPrompt);
        Assert.Contains("0.73", prompt.UserPrompt);
    }

    [Fact]
    public async Task ContextBuilder_UsesRevalidatedProjectionNotRawSnapshot()
    {
        var corpus = await Corpus("drug A");
        var raw = Assert.Single(corpus.Snapshot.Evidence) with
        {
            ConfidenceIntervalLower = 0.1234m, ConfidenceIntervalUpper = 0.2345m, ResultSummary = "CI 0.1234 to 0.2345."
        };
        var builder = new SynthesisContextBuilder(new Store(corpus.Snapshot with { Evidence = [raw] }), new SynthesisOptions(), NullLogger<SynthesisContextBuilder>.Instance);
        var context = await builder.BuildAsync(corpus.ResearchRunId, CancellationToken.None);
        var projected = Assert.Single(Assert.Single(context.Studies).Evidence);
        Assert.Null(projected.ConfidenceIntervalLower);
        Assert.Null(projected.ConfidenceIntervalUpper);
        Assert.DoesNotContain("0.1234", projected.ResultSummary);
        Assert.DoesNotContain("0.1234", ResearchSynthesisPrompt.Create(context).UserPrompt);
    }

    [Fact]
    public async Task Assessor_RequiresTruthfulSourceScope()
    {
        var corpus = await Corpus("drug A");
        var sources = corpus.SourceMaterials.Select(source => source with { Type = SourceMaterialType.StructuredFullText }).ToArray();
        Assert.Equal(0, new QuantitativeEvidenceAssessor().Assess(corpus with { SourceMaterials = sources }).EligibleEvidenceCount);
    }

    [Fact]
    public void Prompt_DoesNotCarryRejectedCiFromRawSummary()
    {
        const string source = "Mortality OR 0.73 (95% CI 0.55 to 0.96).";
        var accepted = Accept(Finding(source) with { ConfidenceIntervalLower = 0.1m, ConfidenceIntervalUpper = 0.2m, ResultSummary = "Mortality OR 0.73 with CI 0.1 to 0.2." });
        Assert.Null(accepted.ConfidenceIntervalLower);
        var evidence = Evidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), accepted);
        var context = new SynthesisContext(evidence.ResearchRunId, Guid.NewGuid(), "Does treatment affect mortality?", null,
            new(1, 1, 0, 1, 1, 1, 1, 0, 0, 0, 1, 0), new(["Fixture"], true, false, false, false, 1),
            [new(evidence.StudyId, "Study", null, null, null, null, null, [], [], "Fixture", null, [evidence])], [], []);
        Assert.DoesNotContain("CI 0.1 to 0.2", ResearchSynthesisPrompt.Create(context).UserPrompt);
    }

    internal static EvidenceFindingDraft Finding(string source, string outcome = "Mortality", string measure = "OR", decimal effect = 0.73m, decimal lower = 0.55m, decimal upper = 0.96m) =>
        new(outcome, "Reported result.", source, "Positive", "adults", "drug A", "placebo", "randomized controlled trial", null, measure, effect, lower, upper, null, 0.95m);

    private static decimal Decimal(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static NumericGroundingFact Fact(EvidenceFindingDraft draft, NumericGroundingField field)
    {
        var anchor = new SourceAnchorResolver().Resolve(Guid.NewGuid(), draft.SupportingText!, draft.SupportingText!).Anchor!;
        return Assert.Single(new SemanticNumericGroundingVerifier().Verify(draft, anchor).Facts, fact => fact.Field == field);
    }

    private static AcceptedEvidenceFinding Accept(EvidenceFindingDraft draft) => Assert.Single(new EvidenceExtractionDraftValidator().Validate(Context(draft.SupportingText!), new([draft])));

    internal static EvidenceExtractionStudyContext Context(string source, Guid? sourceId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Does treatment affect mortality?", null, Guid.NewGuid(), sourceId ?? Guid.NewGuid(), EvidenceSourceScope.Abstract,
            "Fixture", source, SourceMaterial.ComputeContentHash(source), false, ["Abstract"], "Study", source, null, null, null, null, null, [], [], "Fixture");

    internal static SynthesisEvidenceContext Evidence(Guid run, Guid study, Guid extraction, AcceptedEvidenceFinding finding) =>
        new(Guid.NewGuid(), run, study, extraction, finding.Outcome, finding.ResultSummary, finding.SupportingText, finding.Direction, EvidenceSourceScope.Abstract,
            DateTimeOffset.UtcNow, finding.Population, finding.ExposureOrIntervention, finding.Comparator, finding.StudyDesign, finding.SampleSize,
            finding.EffectMeasure, finding.EffectValue, finding.ConfidenceIntervalLower, finding.ConfidenceIntervalUpper, finding.PValue, finding.ConfidenceLevel,
            finding.ReportedStandardError, finding.PValueOperator, finding.NumericGrounding, finding.Timepoint);

    internal static async Task<EvidenceCorpus> Corpus(params string[] interventions)
        => await CorpusFor(interventions.Select(intervention => ((string?)intervention, (string?)"12 weeks", "placebo")).ToArray());

    internal static async Task<EvidenceCorpus> CorpusFor(params (string? Intervention, string? Timepoint, string Comparator)[] contexts)
    {
        var run = Guid.NewGuid();
        var studies = new List<SynthesisStudySnapshot>();
        var evidence = new List<SynthesisEvidenceContext>();
        var extractions = new List<SynthesisExtractionSnapshot>();
        var sources = new List<SynthesisSourceMaterialSnapshot>();
        foreach (var item in contexts)
        {
            var study = Guid.NewGuid();
            var extraction = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var source = $"In adults, {item.Intervention} versus {item.Comparator} at {item.Timepoint}: Mortality OR 0.73 (95% CI 0.55 to 0.96).";
            var accepted = Assert.Single(new EvidenceExtractionDraftValidator().Validate(Context(source, sourceId), new([Finding(source) with { ExposureOrIntervention = item.Intervention, Comparator = item.Comparator, Timepoint = item.Timepoint }])));
            studies.Add(new(study, "Study", null, null, null, null, null, [], [], "Fixture", DateTimeOffset.UtcNow));
            evidence.Add(Evidence(run, study, extraction, accepted));
            extractions.Add(new(extraction, run, study, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, sourceId, 1, true));
            sources.Add(new(sourceId, study, SourceMaterialType.Abstract, "Fixture", null, SourceMaterial.ComputeContentHash(source), 1, false, true, source));
        }
        var snapshot = new SynthesisCorpusSnapshot(run, Guid.NewGuid(), "Question", null, studies, evidence, [], [], extractions, sources);
        return await new EvidenceCorpusBuilder(new Store(snapshot)).BuildAsync(run, CancellationToken.None);
    }

    internal sealed class Store(SynthesisCorpusSnapshot snapshot) : ISynthesisCorpusStore
    {
        public Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private static SynthesisContext Synthesis(SynthesisEvidenceContext evidence) => new(evidence.ResearchRunId, Guid.NewGuid(), "Does treatment affect mortality?", null,
        new(1, 1, 0, 1, 1, 1, 1, 0, 0, 0, 1, 0), new(["Fixture"], true, false, false, false, 1),
        [new(evidence.StudyId, "Study", null, null, null, null, null, [], [], "Fixture", null, [evidence])], [], []);
    private sealed class PromptCapturedException : Exception;
    private sealed class CapturingClient : IStructuredLlmClient
    {
        public StructuredLlmRequest? Request { get; private set; }
        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            throw new PromptCapturedException();
        }
    }
}

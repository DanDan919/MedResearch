using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Evaluation;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class EvidenceEvaluationSignalBuilderTests
{
    [Fact]
    public void Build_DerivesObservableSignalsFromGroundedEvidence()
    {
        var context = CreateContext([
            CreateEvidence(comparator: "placebo", sampleSize: 120, effectValue: 1.42m, ciLower: 1.01m, ciUpper: 1.83m, pValue: 0.03m)
        ], ["Randomized Controlled Trial"]);

        var signals = new EvidenceEvaluationSignalBuilder().Build(context);

        Assert.Equal(EvidenceSourceScope.Abstract, signals.SourceScope);
        Assert.True(signals.HasSampleSize);
        Assert.True(signals.HasEffectEstimate);
        Assert.True(signals.HasConfidenceInterval);
        Assert.True(signals.HasPValue);
        Assert.True(signals.HasComparator);
        Assert.Equal(StudyDesignClassification.RandomizedControlledTrial, signals.MetadataStudyDesignHint);
    }

    [Fact]
    public void Build_RecordsAbstractSourceLimitation()
    {
        var context = CreateContext([], ["Journal Article"]);

        var signals = new EvidenceEvaluationSignalBuilder().Build(context);

        Assert.Contains(signals.ReportingLimitations, item => item.Contains("abstract-level only", StringComparison.OrdinalIgnoreCase));
    }

    internal static EvaluationStudyContext CreateContext(
        IReadOnlyCollection<EvaluationEvidenceContext> evidence,
        IReadOnlyCollection<string>? publicationTypes = null,
        EvidenceSourceScope sourceScope = EvidenceSourceScope.Abstract,
        string? abstractText = "A randomized double-blind trial reported improved recall in 120 adults with p value 0.03.")
    {
        return new EvaluationStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does sleep improve recall in adults?",
            new EvaluationPlanContext("adults", "sleep", "placebo", ["recall"], ["randomized controlled trial"], []),
            Guid.NewGuid(),
            "Sleep and recall",
            abstractText,
            "12345678",
            "10.1000/example",
            "Journal",
            new DateOnly(2026, 1, 1),
            publicationTypes ?? ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed",
            EvidenceExtractionStatus.Completed,
            null,
            sourceScope,
            "evidence-extractor-v1",
            evidence);
    }

    internal static EvaluationEvidenceContext CreateEvidence(
        string? comparator = "placebo",
        int? sampleSize = 120,
        decimal? effectValue = null,
        decimal? ciLower = null,
        decimal? ciUpper = null,
        decimal? pValue = null)
    {
        return new EvaluationEvidenceContext(
            Guid.NewGuid(),
            "recall",
            "Recall improved after sleep.",
            "reported improved recall in 120 adults",
            EvidenceDirection.Positive,
            "adults",
            "sleep",
            comparator,
            "randomized controlled trial",
            sampleSize,
            effectValue.HasValue ? "mean difference" : null,
            effectValue,
            ciLower,
            ciUpper,
            pValue,
            true);
    }
}

public sealed class EvidenceEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_AcceptsValidEvaluationAndPreservesAuthoritativeIdentity()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var llm = new FakeStructuredLlmClient(CreateValidDraft(context));
        var evaluator = CreateEvaluator(llm);

        var result = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(context.ResearchRunId, result.ResearchRunId);
        Assert.Equal(context.StudyId, result.StudyId);
        Assert.Equal(EvidenceEvaluationStatus.Completed, result.Status);
        Assert.Equal("FakeLLM", result.EvaluatorProvider);
        Assert.Equal("fake-model", result.EvaluatorModel);
        Assert.Equal(EvidenceEvaluationPrompt.Version, result.PromptVersion);
        Assert.Single(result.EvidenceIds);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesUnknownInsufficientSourceAndNotApplicableSemanticsForAbstract()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ], ["Journal Article"], abstractText: "An observational cohort reported improved recall in 120 adults.");
        var draft = CreateValidDraft(context) with
        {
            StudyDesign = nameof(StudyDesignClassification.Cohort),
            Randomization = nameof(MethodologicalAssessmentState.Unknown),
            Blinding = nameof(MethodologicalAssessmentState.Unknown),
            AllocationConcealment = nameof(MethodologicalAssessmentState.Unknown),
            AttritionMissingData = nameof(MethodologicalAssessmentState.Unknown),
            OverallConfidence = nameof(MethodologicalConfidence.Moderate)
        };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        var result = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(MethodologicalAssessmentState.NotApplicable, result.Randomization);
        Assert.Equal(MethodologicalAssessmentState.NotApplicable, result.AllocationConcealment);
        Assert.Equal(MethodologicalAssessmentState.InsufficientSource, result.Blinding);
        Assert.Equal(MethodologicalAssessmentState.InsufficientSource, result.AttritionMissingData);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsUnsupportedEnumCategory()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var draft = CreateValidDraft(context) with { Precision = "Excellent" };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<EvidenceEvaluationValidationException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsArbitraryQualityScore()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var draft = CreateValidDraft(context) with { QualityScore = 0.87m };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<EvidenceEvaluationValidationException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsStudyIdentityReplacement()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var draft = CreateValidDraft(context) with { StudyId = Guid.NewGuid().ToString() };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<EvidenceEvaluationValidationException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_SkipsNoExtractedEvidenceWithoutCallingLlm()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([]);
        var llm = new FakeStructuredLlmClient(CreateValidDraft(context));
        var evaluator = CreateEvaluator(llm);

        var result = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(EvidenceEvaluationStatus.Skipped, result.Status);
        Assert.Equal(EvidenceEvaluationSkipReason.NoExtractedEvidence, result.SkipReason);
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsSourceAbsenceConvertedToConcern()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var draft = CreateValidDraft(context) with
        {
            SampleInformation = nameof(MethodologicalAssessmentState.SomeConcern),
            Rationale = "Sample details are not reported in the abstract."
        };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<EvidenceEvaluationValidationException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsUngroundedAuthorReportedLimitation()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var draft = CreateValidDraft(context) with { AuthorReportedLimitations = ["single-center design"] };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<EvidenceEvaluationValidationException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotAllowPValueAloneToCreateHigherConfidence()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence(comparator: null, sampleSize: null, pValue: 0.03m)
        ], abstractText: "Recall improved with p value 0.03.");
        var draft = CreateValidDraft(context) with
        {
            OverallConfidence = nameof(MethodologicalConfidence.Higher),
            SampleInformation = nameof(MethodologicalAssessmentState.Unknown),
            Precision = nameof(MethodologicalAssessmentState.Unknown),
            ComparatorPresence = nameof(ComparatorPresence.Unclear),
            ComparatorDescription = null
        };
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(draft));

        var result = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.NotEqual(MethodologicalConfidence.Higher, result.OverallConfidence);
    }

    [Fact]
    public async Task EvaluateAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(CreateValidDraft(context)));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(context, cancellation.Token));
    }

    [Fact]
    public async Task EvaluateAsync_ProviderFailurePropagates()
    {
        var context = EvidenceEvaluationSignalBuilderTests.CreateContext([
            EvidenceEvaluationSignalBuilderTests.CreateEvidence()
        ]);
        var evaluator = CreateEvaluator(new FakeStructuredLlmClient(CreateValidDraft(context), new StructuredLlmException("provider failed")));

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            evaluator.EvaluateAsync(context, CancellationToken.None));
    }

    private static EvidenceEvaluator CreateEvaluator(IStructuredLlmClient llm)
    {
        return new EvidenceEvaluator(
            llm,
            new EvidenceEvaluationSignalBuilder(),
            new EvidenceEvaluationDraftValidator(),
            NullLogger<EvidenceEvaluator>.Instance);
    }

    private static EvidenceEvaluationDraft CreateValidDraft(EvaluationStudyContext context)
    {
        return new EvidenceEvaluationDraft(
            context.ResearchRunId.ToString(),
            context.StudyId.ToString(),
            nameof(StudyDesignClassification.RandomizedControlledTrial),
            nameof(MethodologicalAssessmentState.Favorable),
            nameof(ComparatorPresence.Present),
            "placebo",
            nameof(MethodologicalAssessmentState.Favorable),
            nameof(MethodologicalAssessmentState.Unknown),
            nameof(MethodologicalAssessmentState.Unknown),
            nameof(MethodologicalAssessmentState.Unknown),
            nameof(MethodologicalAssessmentState.Unknown),
            nameof(DirectnessRating.Direct),
            nameof(MethodologicalConfidence.Moderate),
            "The source reports randomized design and direct evidence, while detailed bias domains are not fully available from the abstract.",
            ["Detailed methods are unavailable from the abstract."],
            []);
    }

    private sealed class FakeStructuredLlmClient : IStructuredLlmClient
    {
        private readonly object _value;
        private readonly Exception? _exception;

        public FakeStructuredLlmClient(object value, Exception? exception = null)
        {
            _value = value;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(
            StructuredLlmRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            if (_exception is not null)
            {
                throw _exception;
            }

            Assert.Equal(EvidenceEvaluationPrompt.Version, request.PromptVersion);
            return Task.FromResult(new StructuredGenerationResult<T>(
                (T)_value,
                new StructuredLlmProviderMetadata("FakeLLM", "fake-model", "response-1", DateTimeOffset.UtcNow)));
        }
    }
}

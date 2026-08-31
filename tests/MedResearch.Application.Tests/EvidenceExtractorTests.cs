using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class EvidenceExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsGroundedFindingFromAbstract()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([
            new EvidenceFindingDraft(
                "working memory",
                "Sleep restriction reduced working memory accuracy.",
                "Sleep restriction reduced working memory accuracy in 120 adults.",
                "Negative",
                "adults",
                "sleep restriction",
                null,
                "controlled trial",
                120,
                null,
                null,
                null,
                null,
                null)
        ]));
        var extractor = CreateExtractor(llm);

        var result = await extractor.ExtractAsync(CreateContext("Sleep restriction reduced working memory accuracy in 120 adults."), CancellationToken.None);

        Assert.Equal(EvidenceExtractionStatus.Completed, result.Status);
        Assert.True(result.GroundingValidated);
        Assert.Equal("FakeLLM", result.Provider);
        Assert.Equal("fake-model", result.Model);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(EvidenceDirection.Negative, finding.Direction);
        Assert.Equal(120, finding.SampleSize);
    }

    [Fact]
    public async Task ExtractAsync_LeavesMissingOptionalScientificFieldsNull()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([
            new EvidenceFindingDraft(
                "recall",
                "Recall improved.",
                "Recall improved after sleep.",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null)
        ]));
        var extractor = CreateExtractor(llm);

        var result = await extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(EvidenceDirection.NotReported, finding.Direction);
        Assert.Null(finding.SampleSize);
        Assert.Null(finding.EffectValue);
        Assert.Null(finding.PValue);
    }

    [Fact]
    public async Task ExtractAsync_SkipsStudyWithoutUsableScientificTextWithoutCallingLlm()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([]));
        var extractor = CreateExtractor(llm);

        var result = await extractor.ExtractAsync(CreateContext(null), CancellationToken.None);

        Assert.Equal(EvidenceExtractionStatus.Skipped, result.Status);
        Assert.Equal(EvidenceExtractionSkipReason.NoExtractableText, result.SkipReason);
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_RejectsFabricatedSupportingExcerpt()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([
            new EvidenceFindingDraft("recall", "Recall improved.", "Recall was cured.", "Positive", null, null, null, null, null, null, null, null, null, null)
        ]));
        var extractor = CreateExtractor(llm);

        await Assert.ThrowsAsync<EvidenceGroundingValidationException>(() =>
            extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_RejectsBlankSupportingExcerpt()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([
            new EvidenceFindingDraft("recall", "Recall improved.", " ", "Positive", null, null, null, null, null, null, null, null, null, null)
        ]));
        var extractor = CreateExtractor(llm);

        await Assert.ThrowsAsync<EvidenceExtractionValidationException>(() =>
            extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_RejectsExcessiveFindingCount()
    {
        var findings = Enumerable.Range(0, 13)
            .Select(index => new EvidenceFindingDraft($"outcome {index}", "Recall improved.", "Recall improved after sleep.", "Positive", null, null, null, null, null, null, null, null, null, null))
            .ToArray();
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft(findings));
        var extractor = CreateExtractor(llm);

        await Assert.ThrowsAsync<EvidenceExtractionValidationException>(() =>
            extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_DeduplicatesDuplicateFindings()
    {
        var duplicate = new EvidenceFindingDraft("recall", "Recall improved.", "Recall improved after sleep.", "Positive", null, null, null, null, null, null, null, null, null, null);
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([duplicate, duplicate]));
        var extractor = CreateExtractor(llm);

        var result = await extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None);

        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task ExtractAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var extractor = CreateExtractor(new FakeStructuredLlmClient(new EvidenceExtractionDraft([])));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            extractor.ExtractAsync(CreateContext("Recall improved after sleep."), cancellation.Token));
    }

    [Fact]
    public async Task ExtractAsync_ProviderFailurePropagates()
    {
        var extractor = CreateExtractor(new FakeStructuredLlmClient(new EvidenceExtractionDraft([]), new StructuredLlmException("provider failed")));

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_NullsUnsupportedNumericValues()
    {
        var llm = new FakeStructuredLlmClient(new EvidenceExtractionDraft([
            new EvidenceFindingDraft(
                "recall",
                "Recall improved.",
                "Recall improved after sleep.",
                "Positive",
                null,
                null,
                null,
                null,
                120,
                "mean difference",
                1.42m,
                null,
                null,
                0.03m)
        ]));
        var extractor = CreateExtractor(llm);

        var result = await extractor.ExtractAsync(CreateContext("Recall improved after sleep."), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.SampleSize);
        Assert.Null(finding.EffectValue);
        Assert.Null(finding.PValue);
    }

    private static EvidenceExtractor CreateExtractor(IStructuredLlmClient llm)
    {
        return new EvidenceExtractor(
            llm,
            new EvidenceExtractionDraftValidator(),
            NullLogger<EvidenceExtractor>.Instance);
    }

    private static EvidenceExtractionStudyContext CreateContext(string? abstractText)
    {
        return new EvidenceExtractionStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does sleep affect memory?",
            new EvidenceExtractionPlanContext("adults", "sleep", null, ["memory"], ["controlled trial"], []),
            Guid.NewGuid(),
            "Sleep and memory",
            abstractText,
            "12345678",
            "10.1000/example",
            "Journal",
            new DateOnly(2026, 1, 1),
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
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

            Assert.Equal(EvidenceExtractionPrompt.Version, request.PromptVersion);
            return Task.FromResult(new StructuredGenerationResult<T>(
                (T)_value,
                new StructuredLlmProviderMetadata("FakeLLM", "fake-model", "response-1", DateTimeOffset.UtcNow)));
        }
    }
}

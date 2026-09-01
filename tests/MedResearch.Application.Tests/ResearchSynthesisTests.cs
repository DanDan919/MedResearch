using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class SynthesisContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_IncludesOnlyCurrentRunValidatedEvidenceAndComputesCoverage()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId, [CreateEvidence(runId, studyId, evidenceId, EvidenceDirection.Positive)]);
        var builder = CreateBuilder(snapshot);

        var context = await builder.BuildAsync(runId, CancellationToken.None);

        Assert.Equal(runId, context.ResearchRunId);
        Assert.Equal(1, context.Statistics.DiscoveredStudyCount);
        Assert.Equal(1, context.Statistics.EvidenceFindingCount);
        Assert.Equal(1, context.Statistics.IncludedEvidenceFindingCount);
        Assert.Equal(["PubMed"], context.SourceCoverage.SearchedSources);
        Assert.True(context.SourceCoverage.UsesAbstractLevelEvidenceOnly);
        Assert.Contains(context.DeterministicLimitations, item => item.Contains("PubMed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_RejectsEvidenceFromAnotherRun()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId, [CreateEvidence(Guid.NewGuid(), studyId, Guid.NewGuid(), EvidenceDirection.Positive)]);
        var builder = CreateBuilder(snapshot);

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            builder.BuildAsync(runId, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_UnknownAndNotReportedDirectionsAreNotCountedAsNegative()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId, [CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.NotReported)]);
        var context = await CreateBuilder(snapshot).BuildAsync(runId, CancellationToken.None);

        var outcome = Assert.Single(context.OutcomeDirectionSummaries);
        Assert.Equal(0, outcome.NegativeCount);
        Assert.Equal(1, outcome.NotReportedCount);
    }

    [Fact]
    public async Task BuildAsync_OpposingDirectionsForSameNormalizedOutcomeProduceConflictSignal()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var evidence = new[]
        {
            CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.Positive),
            CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.Negative)
        };

        var context = await CreateBuilder(CreateSnapshot(runId, evidence)).BuildAsync(runId, CancellationToken.None);

        var outcome = Assert.Single(context.OutcomeDirectionSummaries);
        Assert.Equal(SynthesisConflictStatus.Present, outcome.ConflictStatus);
        Assert.True(context.SourceCoverage.PotentialConflictDetected);
    }

    [Fact]
    public async Task BuildAsync_DoesNotSemanticallyMergeDifferentOutcomeNames()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var evidence = new[]
        {
            CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.Positive, "executive function"),
            CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.Positive, "working memory")
        };

        var context = await CreateBuilder(CreateSnapshot(runId, evidence)).BuildAsync(runId, CancellationToken.None);

        Assert.Equal(["executive function", "working memory"], context.OutcomeDirectionSummaries.Select(summary => summary.Outcome).ToArray());
    }

    [Fact]
    public async Task BuildAsync_UsesDeterministicOrderingAndRecordsTruncation()
    {
        var runId = Guid.NewGuid();
        var firstStudy = Guid.NewGuid();
        var secondStudy = Guid.NewGuid();
        var evidence = new[]
        {
            CreateEvidence(runId, secondStudy, Guid.Parse("22222222-2222-2222-2222-222222222222"), EvidenceDirection.Positive),
            CreateEvidence(runId, firstStudy, Guid.Parse("11111111-1111-1111-1111-111111111111"), EvidenceDirection.Positive)
        };
        var snapshot = CreateSnapshot(runId, evidence, [firstStudy, secondStudy]);
        var builder = CreateBuilder(snapshot, new SynthesisOptions { MaxStudies = 1, MaxEvidenceFindings = 1 });

        var context = await builder.BuildAsync(runId, CancellationToken.None);

        Assert.Single(context.Studies);
        Assert.True(context.SourceCoverage.EvidenceTruncated);
        Assert.Equal(firstStudy, context.Studies.First().StudyId);
    }

    private static SynthesisContextBuilder CreateBuilder(SynthesisCorpusSnapshot snapshot, SynthesisOptions? options = null)
    {
        return new SynthesisContextBuilder(new StaticSynthesisCorpusStore(snapshot), options ?? new SynthesisOptions(), NullLogger<SynthesisContextBuilder>.Instance);
    }

    private static SynthesisCorpusSnapshot CreateSnapshot(Guid runId, IReadOnlyCollection<SynthesisEvidenceContext> evidence, IReadOnlyCollection<Guid>? studyIds = null)
    {
        studyIds ??= evidence.Select(item => item.StudyId).Distinct().ToArray();
        var studies = studyIds.Select((studyId, index) => new SynthesisStudySnapshot(
            studyId,
            $"Study {index}",
            (12345678 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"10.1000/{index}",
            "Journal",
            new DateOnly(2026, 1, 1),
            index == 0 ? ["Systematic Review"] : ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed",
            DateTimeOffset.UtcNow.AddMinutes(index)))
            .ToArray();
        var evaluations = studies.Select(study => new SynthesisEvaluationContext(
            Guid.NewGuid(),
            runId,
            study.StudyId,
            EvidenceEvaluationStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            study.PublicationTypes.Contains("Systematic Review") ? StudyDesignClassification.SystematicReview : StudyDesignClassification.Cohort,
            MethodologicalAssessmentState.Unknown,
            ComparatorPresence.Unclear,
            MethodologicalAssessmentState.NotApplicable,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.NotApplicable,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.MostlyDirect,
            MethodologicalConfidence.InsufficientInformation,
            evidence.Where(item => item.StudyId == study.StudyId).Select(item => item.EvidenceId).ToArray(),
            ["Abstract-level source scope."],
            2,
            3)).ToArray();
        var extractions = studies.Select(study => new SynthesisExtractionSnapshot(Guid.NewGuid(), runId, study.StudyId, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, evidence.Count(item => item.StudyId == study.StudyId), true)).ToArray();

        return new SynthesisCorpusSnapshot(
            runId,
            Guid.NewGuid(),
            "Does sleep improve recall?",
            new SynthesisPlanContext(Guid.NewGuid(), "adults", "sleep", "wakefulness", ["recall"], ["controlled trial"], ["sleep recall"], []),
            studies,
            evidence,
            evaluations,
            [new SynthesisSearchSnapshot(Guid.NewGuid(), runId, "PubMed", "sleep recall", DateTimeOffset.UtcNow, studies.Length, studies.Length, 0)],
            extractions);
    }

    private static SynthesisEvidenceContext CreateEvidence(Guid runId, Guid studyId, Guid evidenceId, EvidenceDirection direction, string outcome = "recall")
    {
        return new SynthesisEvidenceContext(evidenceId, runId, studyId, outcome, "Reported result.", "reported result", direction, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null);
    }

    private sealed class StaticSynthesisCorpusStore : ISynthesisCorpusStore
    {
        private readonly SynthesisCorpusSnapshot _snapshot;

        public StaticSynthesisCorpusStore(SynthesisCorpusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }
    }
}

public sealed class ResearchSynthesizerTests
{
    [Fact]
    public async Task SynthesizeAsync_ValidDraftBecomesTraceableReport()
    {
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Positive)]);
        var client = new FakeStructuredLlmClient(CreateValidDraft(evidenceId));
        var synthesizer = CreateSynthesizer(client);

        var result = await synthesizer.SynthesizeAsync(context, CancellationToken.None);

        Assert.Equal(ResearchReportStatus.Completed, result.Status);
        Assert.Equal(ResearchSynthesisPrompt.Version, client.Request?.PromptVersion);
        var claim = Assert.Single(result.Claims, claim => claim.ClaimType == ResearchReportClaimType.Conclusion);
        Assert.Equal([evidenceId], claim.EvidenceIds);
        Assert.Equal("FakeLLM", result.SynthesizerProvider);
    }

    [Fact]
    public async Task SynthesizeAsync_ZeroValidatedEvidenceCreatesInsufficientEvidenceWithoutLlmCall()
    {
        var context = CreateContext([]);
        var client = new FakeStructuredLlmClient(CreateValidDraft(Guid.NewGuid()));
        var synthesizer = CreateSynthesizer(client);

        var result = await synthesizer.SynthesizeAsync(context, CancellationToken.None);

        Assert.Equal(ResearchReportStatus.InsufficientEvidence, result.Status);
        Assert.Equal(ResearchReportInsufficientEvidenceReason.NoValidatedEvidence, result.InsufficientEvidenceReason);
        Assert.Null(client.Request);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsNonexistentEvidenceReference()
    {
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvidenceDirection.Positive)]);
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(CreateValidDraft(Guid.NewGuid())));

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsClaimWithoutEvidence()
    {
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvidenceDirection.Positive)]);
        var draft = CreateValidDraft(context.Studies.SelectMany(study => study.Evidence).First().EvidenceId) with
        {
            Claims = [CreateClaimDraft(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, [])]
        };
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsFloatingConclusionWithoutConclusionClaim()
    {
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Positive)]);
        var draft = CreateValidDraft(evidenceId) with
        {
            Claims = [CreateClaimDraft(ResearchReportClaimType.Finding, ResearchReportClaimDirection.Positive, [evidenceId])]
        };
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsPositiveClaimSupportedOnlyByNegativeEvidence()
    {
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Negative)]);
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(CreateValidDraft(evidenceId)));

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_AllowsConflictClaimWithOpposingEvidence()
    {
        var runId = Guid.NewGuid();
        var studyId = Guid.NewGuid();
        var positive = Guid.NewGuid();
        var negative = Guid.NewGuid();
        var context = CreateContext([
            CreateEvidence(runId, studyId, positive, EvidenceDirection.Positive),
            CreateEvidence(runId, studyId, negative, EvidenceDirection.Negative)
        ]);
        var draft = CreateValidDraft(positive) with
        {
            Claims = [
                CreateClaimDraft(ResearchReportClaimType.Conflict, ResearchReportClaimDirection.Mixed, [positive, negative]),
                CreateClaimDraft(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Mixed, [positive, negative])
            ]
        };
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(draft));

        var result = await synthesizer.SynthesizeAsync(context, CancellationToken.None);

        Assert.Contains(result.Claims, claim => claim.ClaimType == ResearchReportClaimType.Conflict);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsModelSuppliedCitationMetadata()
    {
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Positive)]);
        var draft = CreateValidDraft(evidenceId) with
        {
            Claims = [CreateClaimDraft(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, [evidenceId]) with { Pmid = "99999999", Doi = "10.fake/model" }]
        };
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(draft));

        await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() =>
            synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_ProviderFailurePropagates()
    {
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Positive)]);
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(CreateValidDraft(evidenceId), new StructuredLlmException("provider failed")));

        await Assert.ThrowsAsync<StructuredLlmException>(() => synthesizer.SynthesizeAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var evidenceId = Guid.NewGuid();
        var context = CreateContext([CreateEvidence(Guid.NewGuid(), Guid.NewGuid(), evidenceId, EvidenceDirection.Positive)]);
        var synthesizer = CreateSynthesizer(new FakeStructuredLlmClient(CreateValidDraft(evidenceId)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => synthesizer.SynthesizeAsync(context, cancellation.Token));
    }

    private static ResearchSynthesizer CreateSynthesizer(FakeStructuredLlmClient client, SynthesisOptions? options = null)
    {
        var validator = new ResearchReportDraftValidator(options ?? new SynthesisOptions());
        return new ResearchSynthesizer(client, validator, NullLogger<ResearchSynthesizer>.Instance);
    }

    private static SynthesisContext CreateContext(IReadOnlyCollection<SynthesisEvidenceContext> evidence)
    {
        var runId = evidence.FirstOrDefault()?.ResearchRunId ?? Guid.NewGuid();
        var studies = evidence.GroupBy(item => item.StudyId).Select(group => new SynthesisStudyContext(group.Key, "Study title", "12345678", "10.1000/example", "Journal", new DateOnly(2026, 1, 1), ["Journal Article"], ["Ada"], "PubMed", null, group.ToArray())).ToArray();
        var statistics = new SynthesisCorpusStatistics(studies.Length, studies.Length, 0, evidence.Count, studies.Length, evidence.Count, 1, 0, 0);
        var coverage = new SynthesisSourceCoverage(["PubMed"], true, false, false, false, 1);
        return new SynthesisContext(runId, Guid.NewGuid(), "Does sleep improve recall?", null, statistics, coverage, studies, [], ["Abstract-level evidence only."]);
    }

    private static SynthesisEvidenceContext CreateEvidence(Guid runId, Guid studyId, Guid evidenceId, EvidenceDirection direction)
    {
        return new SynthesisEvidenceContext(evidenceId, runId, studyId, "recall", "Reported result.", "reported result", direction, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null);
    }

    private static ResearchReportDraft CreateValidDraft(Guid evidenceId)
    {
        return new ResearchReportDraft(
            ResearchReportStatus.Completed.ToString(),
            null,
            "Available evidence suggests a cautious finding.",
            "The included source-grounded finding reports improvement.",
            "No conflicting evidence was included in this bounded context.",
            "The source coverage is abstract-level and limited.",
            "A cautious conclusion is supported by the cited evidence.",
            SynthesisConfidence.Limited.ToString(),
            [CreateClaimDraft(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, [evidenceId])]);
    }

    private static ResearchReportClaimDraft CreateClaimDraft(ResearchReportClaimType type, ResearchReportClaimDirection direction, IReadOnlyCollection<Guid> evidenceIds)
    {
        return new ResearchReportClaimDraft(type.ToString(), direction.ToString(), "Evidence-supported claim.", evidenceIds.Select(id => id.ToString()).ToArray());
    }

    private sealed class FakeStructuredLlmClient : IStructuredLlmClient
    {
        private readonly ResearchReportDraft _draft;
        private readonly Exception? _exception;

        public FakeStructuredLlmClient(ResearchReportDraft draft, Exception? exception = null)
        {
            _draft = draft;
            _exception = exception;
        }

        public StructuredLlmRequest? Request { get; private set; }

        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new StructuredGenerationResult<T>((T)(object)_draft, new StructuredLlmProviderMetadata("FakeLLM", "fake-synthesis-model", "fake-response", DateTimeOffset.UtcNow)));
        }
    }
}
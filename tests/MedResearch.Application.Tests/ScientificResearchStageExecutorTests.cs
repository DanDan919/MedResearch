using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ScientificResearchStageExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PlanningStageInvokesPlannerAndPersistsPlan()
    {
        var plan = CreatePlan(["planned query"]);
        var planner = new RecordingResearchPlanner(plan);
        var planStore = new RecordingResearchPlanStore(plan);
        var executor = CreateExecutor(planner, planStore, new RecordingScientificSource([]), new RecordingSearchResultStore());
        var context = CreateContext(ResearchRunStatus.Planning, "Does sleep deprivation impair memory?");

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(context.ResearchRunId, planner.ResearchRunId);
        Assert.Equal(context.ResearchQuestionId, planner.ResearchQuestionId);
        Assert.Equal("Does sleep deprivation impair memory?", planner.ResearchQuestion);
        Assert.Empty(planStore.FindRequests);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageUsesResearchPlanQueriesInsteadOfResearchQuestionText()
    {
        var plan = CreatePlan(["planned sleep query"]);
        var source = new RecordingScientificSource([
            CreateCandidate("12345678", "10.1000/example")
        ]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);
        var context = CreateContext(ResearchRunStatus.Searching, "raw question should not become the PubMed query");

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(source.Requests);
        Assert.Equal("planned sleep query", source.Requests[0].Query);
        Assert.Single(store.Requests);
        Assert.Equal(plan.Id, store.Requests[0].ResearchPlanId);
        Assert.Equal("planned sleep query", store.Requests[0].Query);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageExecutesMultiplePlanQueriesSequentially()
    {
        var plan = CreatePlan(["query one", "query two"]);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None);

        Assert.Equal(["query one", "query two"], source.Requests.Select(request => request.Query).ToArray());
        Assert.Equal(["query one", "query two"], store.Requests.Select(request => request.Query).ToArray());
        Assert.All(store.Requests, request => Assert.Equal(plan.Id, request.ResearchPlanId));
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStagePersistsZeroResultSearches()
    {
        var plan = CreatePlan(["rare planned topic"]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(
            new RecordingResearchPlanner(plan),
            new RecordingResearchPlanStore(plan),
            new RecordingScientificSource([]),
            store);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None);

        Assert.Single(store.Requests);
        Assert.Empty(store.Requests[0].Candidates);
        Assert.Equal(0, store.Requests[0].ResultCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractingStageExtractsAndPersistsSelectedStudies()
    {
        var study = CreateExtractionStudy("Improved recall was reported in 120 adults.");
        var extractionStore = new RecordingEvidenceExtractionStore([study], 1);
        var finding = new AcceptedEvidenceFinding(
            "recall",
            "Improved recall was reported.",
            "Improved recall was reported in 120 adults.",
            EvidenceDirection.Positive,
            "adults",
            null,
            null,
            null,
            120,
            null,
            null,
            null,
            null,
            null);
        var extractor = new RecordingEvidenceExtractor(new EvidenceExtractionResult(
            study.ResearchRunId,
            study.StudyId,
            EvidenceExtractionStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            "FakeLLM",
            "fake-model",
            EvidenceExtractionPrompt.Version,
            DateTimeOffset.UtcNow,
            true,
            [finding]));
        var executor = CreateExecutor(
            new RecordingResearchPlanner(CreatePlan(["planned query"])),
            new RecordingResearchPlanStore(CreatePlan(["planned query"])),
            new RecordingScientificSource([]),
            new RecordingSearchResultStore(),
            extractor,
            extractionStore);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Extracting), CancellationToken.None);

        Assert.Single(extractor.Contexts);
        Assert.Single(extractionStore.Results);
        Assert.Equal(EvidenceExtractionStatus.Completed, extractionStore.Results[0].Status);
        Assert.Single(extractionStore.Results[0].Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractingStagePersistsNoAbstractSkip()
    {
        var study = CreateExtractionStudy(null);
        var extractionStore = new RecordingEvidenceExtractionStore([study], 1);
        var extractor = new RecordingEvidenceExtractor(new EvidenceExtractionResult(
            study.ResearchRunId,
            study.StudyId,
            EvidenceExtractionStatus.Skipped,
            EvidenceExtractionSkipReason.NoExtractableText,
            EvidenceSourceScope.Abstract,
            null,
            null,
            EvidenceExtractionPrompt.Version,
            DateTimeOffset.UtcNow,
            false,
            []));
        var executor = CreateExecutor(
            new RecordingResearchPlanner(CreatePlan(["planned query"])),
            new RecordingResearchPlanStore(CreatePlan(["planned query"])),
            new RecordingScientificSource([]),
            new RecordingSearchResultStore(),
            extractor,
            extractionStore);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Extracting), CancellationToken.None);

        Assert.Single(extractionStore.Results);
        Assert.Equal(EvidenceExtractionStatus.Skipped, extractionStore.Results[0].Status);
        Assert.Equal(EvidenceExtractionSkipReason.NoExtractableText, extractionStore.Results[0].SkipReason);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractingStageProviderFailurePropagates()
    {
        var study = CreateExtractionStudy("Reported abstract.");
        var executor = CreateExecutor(
            new RecordingResearchPlanner(CreatePlan(["planned query"])),
            new RecordingResearchPlanStore(CreatePlan(["planned query"])),
            new RecordingScientificSource([]),
            new RecordingSearchResultStore(),
            new RecordingEvidenceExtractor(null, new EvidenceExtractionValidationException("provider failed")),
            new RecordingEvidenceExtractionStore([study], 1));

        await Assert.ThrowsAsync<EvidenceExtractionValidationException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Extracting), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_NonImplementedStagesDoNotInvokePlannerSourceExtractorOrEvaluator()
    {
        var plan = CreatePlan(["planned query"]);
        var planner = new RecordingResearchPlanner(plan);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var extractor = new RecordingEvidenceExtractor(null);
        var extractionStore = new RecordingEvidenceExtractionStore([], 0);
        var executor = CreateExecutor(planner, new RecordingResearchPlanStore(plan), source, store, extractor, extractionStore);

        await executor.ExecuteAsync(CreateContext(ResearchRunStatus.Synthesizing), CancellationToken.None);

        Assert.Null(planner.ResearchQuestion);
        Assert.Empty(source.Requests);
        Assert.Empty(store.Requests);
        Assert.Empty(extractor.Contexts);
        Assert.Empty(extractionStore.Results);
    }

    [Fact]
    public async Task ExecuteAsync_SearchingStageFailsSafelyWhenPlanIsMissing()
    {
        var executor = CreateExecutor(
            new RecordingResearchPlanner(CreatePlan(["planned query"])),
            new RecordingResearchPlanStore(null),
            new RecordingScientificSource([]),
            new RecordingSearchResultStore());

        await Assert.ThrowsAsync<ResearchPlanValidationException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SourceFailurePropagatesToProcessorFailurePath()
    {
        var plan = CreatePlan(["planned query"]);
        var source = new RecordingScientificSource([], new ScientificLiteratureSourceException("PubMed request failed."));
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, new RecordingSearchResultStore());

        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagatesWithoutPersistingResults()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var plan = CreatePlan(["planned query"]);
        var source = new RecordingScientificSource([]);
        var store = new RecordingSearchResultStore();
        var executor = CreateExecutor(new RecordingResearchPlanner(plan), new RecordingResearchPlanStore(plan), source, store);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(CreateContext(ResearchRunStatus.Searching), cancellation.Token));

        Assert.Empty(source.Requests);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task ProcessNextQueuedRunAsync_PlanningFailureMarksRunFailed()
    {
        var run = new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
        run.StartPlanning(DateTimeOffset.UtcNow);
        var queue = new ProcessorSearchQueue(new ClaimedResearchRun(run, "Does planning failure mark the run failed?"));
        var planner = new RecordingResearchPlanner(CreatePlan(["planned query"]), new ResearchPlanValidationException("invalid plan"));
        var executor = CreateExecutor(planner, new RecordingResearchPlanStore(null), new RecordingScientificSource([]), new RecordingSearchResultStore());
        var processor = new ResearchRunProcessor(queue, executor, NullLogger<ResearchRunProcessor>.Instance);

        var processed = await processor.ProcessNextQueuedRunAsync("worker-1", CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal("Research processing failed.", run.FailureReason);
        Assert.True(queue.MarkFailedWasCalled);
    }

    private static ResearchStageExecutionContext CreateContext(
        ResearchRunStatus stage,
        string question = "Does chronic sleep deprivation impair working memory in adults?")
    {
        return new ResearchStageExecutionContext(Guid.NewGuid(), Guid.NewGuid(), stage, question, "worker-1");
    }

    private static ScientificResearchStageExecutor CreateExecutor(
        IResearchPlanner planner,
        IResearchPlanStore planStore,
        IScientificLiteratureSource source,
        IScientificSearchResultStore store,
        IEvidenceExtractor? evidenceExtractor = null,
        IEvidenceExtractionStore? evidenceExtractionStore = null,
        IEvidenceEvaluator? evidenceEvaluator = null,
        IEvidenceEvaluationStore? evidenceEvaluationStore = null)
    {
        return new ScientificResearchStageExecutor(
            planner,
            planStore,
            source,
            store,
            evidenceExtractor ?? new RecordingEvidenceExtractor(null),
            evidenceExtractionStore ?? new RecordingEvidenceExtractionStore([], 0),
            new EvidenceExtractionOptions(),
            evidenceEvaluator ?? new RecordingEvidenceEvaluator(null),
            evidenceEvaluationStore ?? new RecordingEvidenceEvaluationStore([], 0),
            new EvidenceEvaluationOptions(),
            NullLogger<ScientificResearchStageExecutor>.Instance);
    }

    private static ResearchPlan CreatePlan(string[] searchQueries)
    {
        return new ResearchPlan(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does chronic sleep deprivation impair working memory in adults?",
            "adults",
            "chronic sleep deprivation",
            "normal sleep",
            ["working memory"],
            ["observational study"],
            searchQueries,
            [],
            "FakeLLM",
            "fake-model",
            ResearchPlannerPrompt.Version,
            DateTimeOffset.UtcNow);
    }

    private static ScientificStudyCandidate CreateCandidate(string pmid, string doi)
    {
        return new ScientificStudyCandidate(
            pmid,
            doi,
            "Sleep and memory",
            null,
            "Journal",
            null,
            2024,
            null,
            null,
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
    }

    private static EvidenceExtractionStudyContext CreateExtractionStudy(string? abstractText)
    {
        return new EvidenceExtractionStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does sleep improve recall?",
            null,
            Guid.NewGuid(),
            "Sleep and recall",
            abstractText,
            "12345678",
            "10.1000/example",
            "Journal",
            new DateOnly(2026, 1, 1),
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
    }


    private static EvaluationStudyContext CreateEvaluationStudy(IReadOnlyCollection<EvaluationEvidenceContext> evidence)
    {
        return new EvaluationStudyContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Does sleep improve recall?",
            new EvaluationPlanContext("adults", "sleep", "wakefulness", ["recall"], ["controlled trial"], []),
            Guid.NewGuid(),
            "Sleep and recall",
            "A randomized trial reported improved recall in 120 adults.",
            "12345678",
            "10.1000/example",
            "Journal",
            new DateOnly(2026, 1, 1),
            ["Randomized Controlled Trial"],
            ["Ada Lovelace"],
            "PubMed",
            EvidenceExtractionStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            EvidenceExtractionPrompt.Version,
            evidence);
    }

    private static EvaluationEvidenceContext CreateEvaluationEvidence(Guid evidenceId)
    {
        return new EvaluationEvidenceContext(
            evidenceId,
            "recall",
            "Recall improved after sleep.",
            "reported improved recall in 120 adults",
            EvidenceDirection.Positive,
            "adults",
            "sleep",
            "wakefulness",
            "randomized controlled trial",
            120,
            null,
            null,
            null,
            null,
            null,
            true);
    }

    private static EvidenceEvaluationResult CreateCompletedEvaluation(EvaluationStudyContext study)
    {
        return new EvidenceEvaluationResult(
            study.ResearchRunId,
            study.StudyId,
            study.Evidence.Select(evidence => evidence.EvidenceId).ToArray(),
            EvidenceEvaluationStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            "FakeLLM",
            "fake-model",
            EvidenceEvaluationPrompt.Version,
            DateTimeOffset.UtcNow,
            StudyDesignClassification.RandomizedControlledTrial,
            MethodologicalAssessmentState.Favorable,
            ComparatorPresence.Present,
            "wakefulness",
            MethodologicalAssessmentState.Favorable,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.Direct,
            MethodologicalConfidence.InsufficientInformation,
            "The abstract supports directness but not detailed bias assessment.",
            ["Current source scope is abstract-level only."],
            [],
            new EvidenceEvaluationSignalSet(EvidenceSourceScope.Abstract, study.Evidence.Count, true, false, false, false, true, StudyDesignClassification.RandomizedControlledTrial, []),
            1,
            3);
    }

    private static EvidenceEvaluationResult CreateSkippedEvaluation(EvaluationStudyContext study)
    {
        return new EvidenceEvaluationResult(
            study.ResearchRunId,
            study.StudyId,
            [],
            EvidenceEvaluationStatus.Skipped,
            EvidenceEvaluationSkipReason.NoExtractedEvidence,
            EvidenceSourceScope.Abstract,
            null,
            null,
            EvidenceEvaluationPrompt.Version,
            DateTimeOffset.UtcNow,
            StudyDesignClassification.Unknown,
            MethodologicalAssessmentState.Unknown,
            ComparatorPresence.Unclear,
            null,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.Unclear,
            MethodologicalConfidence.InsufficientInformation,
            "No source-grounded evidence findings are available.",
            [],
            [],
            new EvidenceEvaluationSignalSet(EvidenceSourceScope.Abstract, 0, false, false, false, false, false, StudyDesignClassification.Unknown, []),
            4,
            5);
    }
    private sealed class RecordingResearchPlanner : IResearchPlanner
    {
        private readonly ResearchPlan _plan;
        private readonly Exception? _exception;

        public RecordingResearchPlanner(ResearchPlan plan, Exception? exception = null)
        {
            _plan = plan;
            _exception = exception;
        }

        public Guid? ResearchRunId { get; private set; }

        public Guid? ResearchQuestionId { get; private set; }

        public string? ResearchQuestion { get; private set; }

        public Task<ResearchPlan> GenerateAndPersistPlanAsync(
            Guid researchRunId,
            Guid researchQuestionId,
            string researchQuestion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResearchRunId = researchRunId;
            ResearchQuestionId = researchQuestionId;
            ResearchQuestion = researchQuestion;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_plan);
        }
    }

    private sealed class RecordingResearchPlanStore : IResearchPlanStore
    {
        private readonly ResearchPlan? _plan;

        public RecordingResearchPlanStore(ResearchPlan? plan)
        {
            _plan = plan;
        }

        public List<Guid> FindRequests { get; } = [];

        public Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            FindRequests.Add(researchRunId);
            return Task.FromResult(_plan);
        }
    }

    private sealed class ProcessorSearchQueue : IResearchRunQueue
    {
        private readonly ClaimedResearchRun _claimedRun;

        public ProcessorSearchQueue(ClaimedResearchRun claimedRun)
        {
            _claimedRun = claimedRun;
        }

        public bool MarkFailedWasCalled { get; private set; }

        public Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            return Task.FromResult<ClaimedResearchRun?>(_claimedRun);
        }

        public Task SaveProgressAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> MarkFailedAsync(Guid researchRunId, string safeFailureReason, DateTimeOffset failedAt, CancellationToken cancellationToken)
        {
            MarkFailedWasCalled = true;
            _claimedRun.Run.Fail(safeFailureReason, failedAt);
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingScientificSource : IScientificLiteratureSource
    {
        private readonly IReadOnlyCollection<ScientificStudyCandidate> _candidates;
        private readonly Exception? _exception;

        public RecordingScientificSource(IReadOnlyCollection<ScientificStudyCandidate> candidates, Exception? exception = null)
        {
            _candidates = candidates;
            _exception = exception;
        }

        public string SourceName => "PubMed";

        public List<ScientificSearchRequest> Requests { get; } = [];

        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new ScientificSearchResult(SourceName, DateTimeOffset.UtcNow, _candidates.Count, _candidates));
        }
    }

    private sealed class RecordingSearchResultStore : IScientificSearchResultStore
    {
        public List<ScientificSearchPersistenceRequest> Requests { get; } = [];

        public Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
            ScientificSearchPersistenceRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ScientificSearchPersistenceResult(request.SearchExecutionId, request.Candidates.Count, 0));
        }
    }

    private sealed class RecordingEvidenceExtractor : IEvidenceExtractor
    {
        private readonly EvidenceExtractionResult? _result;
        private readonly Exception? _exception;

        public RecordingEvidenceExtractor(EvidenceExtractionResult? result, Exception? exception = null)
        {
            _result = result;
            _exception = exception;
        }

        public List<EvidenceExtractionStudyContext> Contexts { get; } = [];

        public Task<EvidenceExtractionResult> ExtractAsync(
            EvidenceExtractionStudyContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result ?? new EvidenceExtractionResult(
                context.ResearchRunId,
                context.StudyId,
                EvidenceExtractionStatus.Skipped,
                EvidenceExtractionSkipReason.NoExtractableText,
                EvidenceSourceScope.Abstract,
                null,
                null,
                EvidenceExtractionPrompt.Version,
                DateTimeOffset.UtcNow,
                false,
                []));
        }
    }

    private sealed class RecordingEvidenceExtractionStore : IEvidenceExtractionStore
    {
        private readonly IReadOnlyCollection<EvidenceExtractionStudyContext> _studies;
        private readonly int _totalCount;

        public RecordingEvidenceExtractionStore(IReadOnlyCollection<EvidenceExtractionStudyContext> studies, int totalCount)
        {
            _studies = studies;
            _totalCount = totalCount;
        }

        public List<EvidenceExtractionResult> Results { get; } = [];

        public Task<EvidenceExtractionWorkItemSet> FindStudiesForExtractionAsync(
            Guid researchRunId,
            string promptVersion,
            int maxStudies,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EvidenceExtractionWorkItemSet(_totalCount, _studies));
        }

        public Task PersistExtractionResultAsync(
            EvidenceExtractionResult result,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Results.Add(result);
            return Task.CompletedTask;
        }

        }

    private sealed class RecordingEvidenceEvaluator : IEvidenceEvaluator
    {
        private readonly EvidenceEvaluationResult? _result;
        private readonly Exception? _exception;

        public RecordingEvidenceEvaluator(EvidenceEvaluationResult? result, Exception? exception = null)
        {
            _result = result;
            _exception = exception;
        }

        public List<EvaluationStudyContext> Contexts { get; } = [];

        public Task<EvidenceEvaluationResult> EvaluateAsync(
            EvaluationStudyContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(context);

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result ?? CreateSkippedEvaluation(context));
        }
    }

    private sealed class RecordingEvidenceEvaluationStore : IEvidenceEvaluationStore
    {
        private readonly IReadOnlyCollection<EvaluationStudyContext> _studies;
        private readonly int _totalCount;

        public RecordingEvidenceEvaluationStore(IReadOnlyCollection<EvaluationStudyContext> studies, int totalCount)
        {
            _studies = studies;
            _totalCount = totalCount;
        }

        public List<EvidenceEvaluationResult> Results { get; } = [];

        public Task<EvidenceEvaluationWorkItemSet> FindStudiesForEvaluationAsync(
            Guid researchRunId,
            string promptVersion,
            int maxStudies,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EvidenceEvaluationWorkItemSet(_totalCount, _studies));
        }

        public Task PersistEvaluationResultAsync(
            EvidenceEvaluationResult result,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Results.Add(result);
            return Task.CompletedTask;
        }
    }
}

using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Planning;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ResearchPlannerTests
{
    private static readonly Guid ResearchRunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResearchQuestionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Question = "Does chronic sleep deprivation impair working memory in adults?";

    [Fact]
    public async Task GenerateAndPersistPlanAsync_ValidStructuredOutputBecomesResearchPlan()
    {
        var provider = new FakeStructuredLlmClient(CreateValidDraft());
        var store = new RecordingResearchPlanStore();
        var planner = CreatePlanner(provider, store);

        var plan = await planner.GenerateAndPersistPlanAsync(ResearchRunId, ResearchQuestionId, Question, CancellationToken.None);

        Assert.Equal(ResearchRunId, plan.ResearchRunId);
        Assert.Equal(ResearchQuestionId, plan.ResearchQuestionId);
        Assert.Equal(Question, plan.OriginalQuestion);
        Assert.Equal("adults", plan.Population);
        Assert.Equal("chronic sleep deprivation", plan.ExposureOrIntervention);
        Assert.Equal("normal or adequate sleep", plan.Comparator);
        Assert.Equal(["working memory performance"], plan.Outcomes);
        Assert.Equal(["observational study", "experimental study"], plan.PreferredStudyTypes);
        Assert.Equal(["\"chronic sleep deprivation\" AND \"working memory\" AND adults"], plan.SearchQueries);
        Assert.Equal(["animal studies"], plan.ExclusionHints);
        Assert.Equal("FakeLLM", plan.Provider);
        Assert.Equal("fake-planner-model", plan.Model);
        Assert.Equal(ResearchPlannerPrompt.Version, plan.PromptVersion);
        Assert.Same(plan, store.SavedPlan);
        Assert.Equal(ResearchPlannerPrompt.Version, provider.Request?.PromptVersion);
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsEmptySearchQueries()
    {
        await AssertInvalidAsync(CreateValidDraft() with { SearchQueries = [] });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsExcessiveSearchQueryCount()
    {
        await AssertInvalidAsync(CreateValidDraft() with
        {
            SearchQueries = ["q1", "q2", "q3", "q4", "q5", "q6"]
        });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsBlankSearchQuery()
    {
        await AssertInvalidAsync(CreateValidDraft() with { SearchQueries = ["valid query", " "] });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RemovesDuplicateSearchQueries()
    {
        var store = new RecordingResearchPlanStore();
        var planner = CreatePlanner(new FakeStructuredLlmClient(CreateValidDraft() with
        {
            SearchQueries = ["sleep AND memory", " sleep AND memory ", "SLEEP AND MEMORY"]
        }), store);

        var plan = await planner.GenerateAndPersistPlanAsync(ResearchRunId, ResearchQuestionId, Question, CancellationToken.None);

        Assert.Equal(["sleep AND memory"], plan.SearchQueries);
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsMalformedQuestionContext()
    {
        await AssertInvalidAsync(CreateValidDraft() with { OriginalQuestion = "Does caffeine improve reaction time?" });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsUnsupportedStudyType()
    {
        await AssertInvalidAsync(CreateValidDraft() with { PreferredStudyTypes = ["miracle evidence"] });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_RejectsProhibitedStableIdentifiersInQueries()
    {
        await AssertInvalidAsync(CreateValidDraft() with { SearchQueries = ["PMID 12345678"] });
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var planner = CreatePlanner(new FakeStructuredLlmClient(CreateValidDraft()), new RecordingResearchPlanStore());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            planner.GenerateAndPersistPlanAsync(ResearchRunId, ResearchQuestionId, Question, cancellation.Token));
    }

    [Fact]
    public async Task GenerateAndPersistPlanAsync_ProviderFailurePropagatesWithoutPersistence()
    {
        var store = new RecordingResearchPlanStore();
        var planner = CreatePlanner(
            new FakeStructuredLlmClient(CreateValidDraft(), new StructuredLlmException("provider failed")),
            store);

        await Assert.ThrowsAsync<StructuredLlmException>(() =>
            planner.GenerateAndPersistPlanAsync(ResearchRunId, ResearchQuestionId, Question, CancellationToken.None));

        Assert.Null(store.SavedPlan);
    }

    [Fact]
    public void ResearchPlanDraft_DoesNotExposeScientificResultFields()
    {
        var propertyNames = typeof(ResearchPlanDraft)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Pmid", propertyNames);
        Assert.DoesNotContain("Doi", propertyNames);
        Assert.DoesNotContain("EffectSize", propertyNames);
        Assert.DoesNotContain("PValue", propertyNames);
        Assert.DoesNotContain("Conclusion", propertyNames);
    }

    private static async Task AssertInvalidAsync(ResearchPlanDraft draft)
    {
        var store = new RecordingResearchPlanStore();
        var planner = CreatePlanner(new FakeStructuredLlmClient(draft), store);

        await Assert.ThrowsAsync<ResearchPlanValidationException>(() =>
            planner.GenerateAndPersistPlanAsync(ResearchRunId, ResearchQuestionId, Question, CancellationToken.None));

        Assert.Null(store.SavedPlan);
    }

    private static ResearchPlanner CreatePlanner(FakeStructuredLlmClient provider, RecordingResearchPlanStore store)
    {
        return new ResearchPlanner(provider, store, NullLogger<ResearchPlanner>.Instance);
    }

    private static ResearchPlanDraft CreateValidDraft()
    {
        return new ResearchPlanDraft(
            Question,
            "adults",
            "chronic sleep deprivation",
            "normal or adequate sleep",
            ["working memory performance"],
            ["observational study", "experimental study"],
            ["\"chronic sleep deprivation\" AND \"working memory\" AND adults"],
            ["animal studies"]);
    }

    private sealed class FakeStructuredLlmClient : IStructuredLlmClient
    {
        private readonly ResearchPlanDraft _draft;
        private readonly Exception? _exception;

        public FakeStructuredLlmClient(ResearchPlanDraft draft, Exception? exception = null)
        {
            _draft = draft;
            _exception = exception;
        }

        public StructuredLlmRequest? Request { get; private set; }

        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(
            StructuredLlmRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new StructuredGenerationResult<T>(
                (T)(object)_draft,
                new StructuredLlmProviderMetadata("FakeLLM", "fake-planner-model", "fake-response-1", DateTimeOffset.UtcNow)));
        }
    }

    private sealed class RecordingResearchPlanStore : IResearchPlanStore
    {
        public ResearchPlan? SavedPlan { get; private set; }

        public Task SaveResearchPlanAsync(ResearchPlan researchPlan, CancellationToken cancellationToken)
        {
            SavedPlan = researchPlan;
            return Task.CompletedTask;
        }

        public Task<ResearchPlan?> FindByResearchRunIdAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult(SavedPlan?.ResearchRunId == researchRunId ? SavedPlan : null);
        }
    }
}

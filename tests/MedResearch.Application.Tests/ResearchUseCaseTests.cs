using MedResearch.Application.Research;
using MedResearch.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ResearchUseCaseTests
{
    [Fact]
    public async Task CreateResearch_CreatesQueuedLinkedRun()
    {
        var store = new CapturingResearchStore();
        var useCase = new CreateResearchUseCase(store, NullLogger<CreateResearchUseCase>.Instance);

        var result = await useCase.ExecuteAsync(
            new CreateResearchCommand("Does chronic sleep deprivation impair working memory in adults?"),
            CancellationToken.None);

        Assert.Equal(ResearchRunStatus.Queued.ToString(), result.Status);
        Assert.NotEqual(Guid.Empty, result.ResearchRunId);
        Assert.NotNull(store.SavedQuestion);
        Assert.NotNull(store.SavedRun);
        Assert.Equal(store.SavedQuestion.Id, store.SavedRun.ResearchQuestionId);
        Assert.Equal(result.ResearchRunId, store.SavedRun.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateResearch_RejectsInvalidQuestion(string? question)
    {
        var store = new CapturingResearchStore();
        var useCase = new CreateResearchUseCase(store, NullLogger<CreateResearchUseCase>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(new CreateResearchCommand(question), CancellationToken.None));

        Assert.Null(store.SavedQuestion);
        Assert.Null(store.SavedRun);
    }

    [Fact]
    public async Task GetResearch_ReturnsNullForUnknownRun()
    {
        var store = new CapturingResearchStore();
        var useCase = new GetResearchUseCase(store, NullLogger<GetResearchUseCase>.Instance);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListResearch_UsesDefaults()
    {
        var store = new CapturingResearchStore();
        var useCase = new ListResearchRunsUseCase(store, NullLogger<ListResearchRunsUseCase>.Instance);

        var result = await useCase.ExecuteAsync(new ListResearchRunsQuery(null, null, null), CancellationToken.None);

        Assert.Equal(ListResearchRunsUseCase.DefaultPage, store.Page);
        Assert.Equal(ListResearchRunsUseCase.DefaultPageSize, store.PageSize);
        Assert.Null(store.Status);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListResearch_ParsesStatusFilter()
    {
        var store = new CapturingResearchStore();
        var useCase = new ListResearchRunsUseCase(store, NullLogger<ListResearchRunsUseCase>.Instance);

        await useCase.ExecuteAsync(
            new ListResearchRunsQuery(2, 10, ResearchRunStatus.Completed.ToString()),
            CancellationToken.None);

        Assert.Equal(2, store.Page);
        Assert.Equal(10, store.PageSize);
        Assert.Equal(ResearchRunStatus.Completed, store.Status);
    }

    [Theory]
    [InlineData(0, 20, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 20, "Done")]
    public async Task ListResearch_RejectsInvalidParameters(int page, int pageSize, string? status)
    {
        var store = new CapturingResearchStore();
        var useCase = new ListResearchRunsUseCase(store, NullLogger<ListResearchRunsUseCase>.Instance);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(new ListResearchRunsQuery(page, pageSize, status), CancellationToken.None));
    }

    private sealed class CapturingResearchStore : IResearchStore
    {
        public ResearchQuestion? SavedQuestion { get; private set; }

        public ResearchRun? SavedRun { get; private set; }

        public Task PersistInitialResearchAsync(
            ResearchQuestion question,
            ResearchRun run,
            CancellationToken cancellationToken)
        {
            SavedQuestion = question;
            SavedRun = run;
            return Task.CompletedTask;
        }

        public Task<ResearchRunDetails?> FindResearchRunAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ResearchRunDetails?>(null);
        }

        public int? Page { get; private set; }

        public int? PageSize { get; private set; }

        public ResearchRunStatus? Status { get; private set; }

        public Task<ResearchRunListResult> ListResearchRunsAsync(
            int page,
            int pageSize,
            ResearchRunStatus? status,
            CancellationToken cancellationToken)
        {
            Page = page;
            PageSize = pageSize;
            Status = status;
            return Task.FromResult(new ResearchRunListResult([], page, pageSize, 0, 0));
        }
    }
}

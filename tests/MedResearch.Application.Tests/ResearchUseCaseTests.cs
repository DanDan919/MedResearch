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
    }
}

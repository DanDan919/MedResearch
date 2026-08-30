using MedResearch.Application.Research;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchStorePersistenceTests
{
    private readonly PostgreSqlFixture _fixture;

    public ResearchStorePersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistInitialResearchAsync_PersistsQuestionAndRun()
    {
        SkipIfPostgreSqlUnavailable();

        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("Does meditation reduce blood pressure in adults?", now);
        var run = new ResearchRun(question.Id, now);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchStore(context);
            await store.PersistInitialResearchAsync(question, run, CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var savedQuestion = await verificationContext.ResearchQuestions
            .SingleAsync(saved => saved.Id == question.Id, CancellationToken.None);
        var savedRun = await verificationContext.ResearchRuns
            .SingleAsync(saved => saved.Id == run.Id, CancellationToken.None);

        Assert.Equal(question.Text, savedQuestion.Text);
        Assert.Equal(question.Id, savedRun.ResearchQuestionId);
        Assert.Equal(ResearchRunStatus.Queued, savedRun.Status);
    }

    [SkippableFact]
    public async Task FindResearchRunAsync_ReturnsPersistedRunWithQuestion()
    {
        SkipIfPostgreSqlUnavailable();

        const string questionText = "Does high-intensity interval training improve insulin sensitivity?";
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion(questionText, now);
        var run = new ResearchRun(question.Id, now);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchStore(context);
            await store.PersistInitialResearchAsync(question, run, CancellationToken.None);
        }

        await using var retrievalContext = _fixture.CreateDbContext();
        var retrievalStore = new EfResearchStore(retrievalContext);
        var result = await retrievalStore.FindResearchRunAsync(run.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(run.Id, result.ResearchRunId);
        Assert.Equal(questionText, result.Question);
        Assert.Equal(ResearchRunStatus.Queued.ToString(), result.Status);
        Assert.InRange(result.CreatedAt, now.AddSeconds(-1), now.AddSeconds(1));
        Assert.Null(result.StartedAt);
        Assert.Null(result.CompletedAt);
        Assert.Null(result.FailureReason);
    }

    [SkippableFact]
    public async Task PersistInitialResearchAsync_RollsBackQuestionWhenRunRelationshipIsInvalid()
    {
        SkipIfPostgreSqlUnavailable();

        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("Does magnesium supplementation improve sleep quality?", now);
        var run = new ResearchRun(Guid.NewGuid(), now);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchStore(context);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.PersistInitialResearchAsync(question, run, CancellationToken.None));
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var questionWasPersisted = await verificationContext.ResearchQuestions
            .AnyAsync(saved => saved.Id == question.Id, CancellationToken.None);

        Assert.False(questionWasPersisted);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}



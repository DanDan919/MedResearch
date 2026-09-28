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
    public async Task OneResearchQuestion_CanOwnMultipleIndependentRuns()
    {
        SkipIfPostgreSqlUnavailable();

        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("Does the same question support independent reruns?", now);
        var firstRun = new ResearchRun(question.Id, now);
        var secondRun = new ResearchRun(question.Id, now.AddMinutes(1));

        await using (var context = _fixture.CreateDbContext())
        {
            context.ResearchQuestions.Add(question);
            context.ResearchRuns.AddRange(firstRun, secondRun);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var runs = await verificationContext.ResearchRuns
            .AsNoTracking()
            .Where(run => run.ResearchQuestionId == question.Id)
            .OrderBy(run => run.CreatedAt)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal([firstRun.Id, secondRun.Id], runs.Select(run => run.Id).ToArray());
        Assert.All(runs, run => Assert.Equal(ResearchRunStatus.Queued, run.Status));
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

    [SkippableFact]
    public async Task ListResearchRunsAsync_ReturnsEmptyPageWhenNoRunsExist()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchStore(context);

        var result = await store.ListResearchRunsAsync(1, 20, null, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [SkippableFact]
    public async Task ListResearchRunsAsync_PaginatesNewestFirstWithDeterministicTieBreaker()
    {
        SkipIfPostgreSqlUnavailable();

        var createdAt = DateTimeOffset.UtcNow;
        var olderQuestion = new ResearchQuestion(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "Older question?", createdAt.AddMinutes(-2));
        var tiedLowerQuestion = new ResearchQuestion(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "Tied lower id question?", createdAt);
        var tiedHigherQuestion = new ResearchQuestion(Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), "Tied higher id question?", createdAt);
        var newestQuestion = new ResearchQuestion(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), "Newest question?", createdAt.AddMinutes(1));

        var olderRun = new ResearchRun(Guid.Parse("11111111-1111-4111-8111-111111111111"), olderQuestion.Id, ResearchRunStatus.Queued, createdAt.AddMinutes(-2), null, null, null);
        var tiedLowerRun = new ResearchRun(Guid.Parse("22222222-2222-4222-8222-222222222222"), tiedLowerQuestion.Id, ResearchRunStatus.Searching, createdAt, createdAt, null, null);
        var tiedHigherRun = new ResearchRun(Guid.Parse("33333333-3333-4333-8333-333333333333"), tiedHigherQuestion.Id, ResearchRunStatus.Failed, createdAt, createdAt, createdAt.AddMinutes(1), "Failed safely.");
        var newestRun = new ResearchRun(Guid.Parse("44444444-4444-4444-8444-444444444444"), newestQuestion.Id, ResearchRunStatus.Completed, createdAt.AddMinutes(1), createdAt.AddMinutes(1), createdAt.AddMinutes(2), null);

        await using (var context = _fixture.CreateDbContext())
        {
            context.ResearchQuestions.AddRange(olderQuestion, tiedLowerQuestion, tiedHigherQuestion, newestQuestion);
            context.ResearchRuns.AddRange(olderRun, tiedLowerRun, tiedHigherRun, newestRun);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var retrievalContext = _fixture.CreateDbContext();
        var store = new EfResearchStore(retrievalContext);

        var firstPage = await store.ListResearchRunsAsync(1, 2, null, CancellationToken.None);
        var secondPage = await store.ListResearchRunsAsync(2, 2, null, CancellationToken.None);
        var beyondPage = await store.ListResearchRunsAsync(4, 2, null, CancellationToken.None);

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal([newestRun.Id, tiedHigherRun.Id], firstPage.Items.Select(item => item.ResearchRunId).ToArray());
        Assert.Equal([tiedLowerRun.Id, olderRun.Id], secondPage.Items.Select(item => item.ResearchRunId).ToArray());
        Assert.Empty(beyondPage.Items);

        var failedItem = firstPage.Items.Single(item => item.ResearchRunId == tiedHigherRun.Id);
        Assert.Equal(tiedHigherQuestion.Id, failedItem.ResearchQuestionId);
        Assert.Equal("Tied higher id question?", failedItem.Question);
        Assert.Equal(ResearchRunStatus.Failed.ToString(), failedItem.Status);
        Assert.Equal("Failed safely.", failedItem.FailureReason);
    }

    [SkippableFact]
    public async Task ListResearchRunsAsync_FiltersByStatusAndDoesNotDuplicateRuns()
    {
        SkipIfPostgreSqlUnavailable();

        var now = DateTimeOffset.UtcNow;
        var completedQuestion = new ResearchQuestion("Completed run?", now);
        var activeQuestion = new ResearchQuestion("Active run?", now.AddMinutes(1));
        var completedRun = new ResearchRun(Guid.NewGuid(), completedQuestion.Id, ResearchRunStatus.Completed, now, now, now.AddMinutes(1), null);
        var activeRun = new ResearchRun(Guid.NewGuid(), activeQuestion.Id, ResearchRunStatus.Searching, now.AddMinutes(1), now.AddMinutes(1), null, null);

        await using (var context = _fixture.CreateDbContext())
        {
            context.ResearchQuestions.AddRange(completedQuestion, activeQuestion);
            context.ResearchRuns.AddRange(completedRun, activeRun);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var retrievalContext = _fixture.CreateDbContext();
        var store = new EfResearchStore(retrievalContext);

        var result = await store.ListResearchRunsAsync(1, 100, ResearchRunStatus.Completed, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(completedRun.Id, item.ResearchRunId);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}



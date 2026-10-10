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
            var store = ResearchCreateTestClient.SharedFixtureStore(context);
            await store.PersistInitialResearchAsync(question, run, ResearchOwnership.LegacyUnownedSubjectId, Guid.NewGuid(), CancellationToken.None);
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
            var store = ResearchCreateTestClient.SharedFixtureStore(context);
            await store.PersistInitialResearchAsync(question, run, ResearchOwnership.LegacyUnownedSubjectId, Guid.NewGuid(), CancellationToken.None);
        }

        await using var retrievalContext = _fixture.CreateDbContext();
        var retrievalStore = new EfResearchStore(retrievalContext);
        var result = await retrievalStore.FindResearchRunAsync(run.Id, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

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
            var store = ResearchCreateTestClient.SharedFixtureStore(context);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.PersistInitialResearchAsync(question, run, ResearchOwnership.LegacyUnownedSubjectId, Guid.NewGuid(), CancellationToken.None));
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var questionWasPersisted = await verificationContext.ResearchQuestions
            .AnyAsync(saved => saved.Id == question.Id, CancellationToken.None);

        Assert.False(questionWasPersisted);
    }

    [SkippableFact]
    public async Task ListResearchRunsAsync_ReturnsEmptyPageBeyondKnownResults()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchStore(context);

        var baseline = await store.ListResearchRunsAsync(1, 1, null, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        var beyondLastPage = baseline.TotalCount + 1;
        var result = await store.ListResearchRunsAsync(beyondLastPage, 1, null, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(beyondLastPage, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(baseline.TotalCount, result.TotalCount);
        Assert.Equal(baseline.TotalPages, result.TotalPages);
    }

    [SkippableFact]
    public async Task ListResearchRunsAsync_PaginatesNewestFirstWithDeterministicTieBreaker()
    {
        SkipIfPostgreSqlUnavailable();

        var createdAt = new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero);
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

        var firstPage = await store.ListResearchRunsAsync(1, 2, null, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        var secondPage = await store.ListResearchRunsAsync(2, 2, null, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        var beyondPageNumber = (firstPage.TotalCount / 2) + 2;
        var beyondPage = await store.ListResearchRunsAsync(beyondPageNumber, 2, null, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

        Assert.Equal([newestRun.Id, tiedHigherRun.Id], firstPage.Items.Select(item => item.ResearchRunId).ToArray());
        Assert.Equal([tiedLowerRun.Id, olderRun.Id], secondPage.Items.Select(item => item.ResearchRunId).ToArray());
        Assert.Empty(beyondPage.Items);
        Assert.True(firstPage.TotalCount >= 4);
        Assert.True(firstPage.TotalPages >= 2);

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

        var now = new DateTimeOffset(2100, 2, 1, 12, 0, 0, TimeSpan.Zero);
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

        var result = await store.ListResearchRunsAsync(1, 100, ResearchRunStatus.Completed, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

        var item = Assert.Single(result.Items, item => item.ResearchRunId == completedRun.Id);
        Assert.Equal(completedRun.Id, item.ResearchRunId);
        Assert.DoesNotContain(result.Items, item => item.ResearchRunId == activeRun.Id);
        Assert.Equal(result.Items.Count, result.Items.Select(item => item.ResearchRunId).Distinct().Count());
        Assert.True(result.TotalCount >= 1);
        Assert.True(result.TotalPages >= 1);
    }

    [SkippableFact]
    public async Task ResearchRunReads_AreScopedToTheQuestionOwner()
    {
        SkipIfPostgreSqlUnavailable();

        var now = DateTimeOffset.UtcNow;
        var questionA = new ResearchQuestion("Private owner A question", now, "UserA");
        var questionB = new ResearchQuestion("Private owner B question", now.AddSeconds(1), "UserB");
        var runA = new ResearchRun(questionA.Id, now);
        var runB = new ResearchRun(questionB.Id, now.AddSeconds(1));

        await using (var context = _fixture.CreateDbContext())
        {
            var store = ResearchCreateTestClient.SharedFixtureStore(context);
            await store.PersistInitialResearchAsync(questionA, runA, "UserA", Guid.NewGuid(), CancellationToken.None);
            await store.PersistInitialResearchAsync(questionB, runB, "UserB", Guid.NewGuid(), CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var storeForRead = new EfResearchStore(verificationContext);
        var ownerARead = await storeForRead.FindResearchRunAsync(runA.Id, "UserA", CancellationToken.None);
        var crossUserRead = await storeForRead.FindResearchRunAsync(runB.Id, "UserA", CancellationToken.None);
        var ownerAList = await storeForRead.ListResearchRunsAsync(1, 100, null, "UserA", CancellationToken.None);

        Assert.NotNull(ownerARead);
        Assert.Null(crossUserRead);
        Assert.Contains(ownerAList.Items, item => item.ResearchRunId == runA.Id);
        Assert.DoesNotContain(ownerAList.Items, item => item.ResearchRunId == runB.Id);
        Assert.Equal(ownerAList.Items.Count, ownerAList.TotalCount);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}



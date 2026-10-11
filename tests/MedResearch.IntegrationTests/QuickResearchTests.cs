using MedResearch.Application.QuickResearch;
using MedResearch.Domain;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

[Collection(QuickResearchPostgreSqlCollection.Name)]
public sealed class QuickResearchTests(PostgreSqlFixture fixture)
{
    private void RequireDatabase() => Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");

    [SkippableFact]
    public async Task StructuredAnswerAndSources_SurviveFreshContext_AndStayOwnerScoped()
    {
        RequireDatabase();
        var owner = Guid.NewGuid().ToString();
        var run = new QuickResearchRun("Synthetic question", owner, DateTimeOffset.UtcNow);
        var source = new QuickResearchSource(Guid.NewGuid(), run.Id, "Q0 fixture only", "https://example.invalid/fixture", true);
        var result = QuickResearchResult.CreateSynthetic(run.Id) with { Sources = [source], Facts = [new("Synthetic fact, not a medical claim", [source.Id])] };
        await using (var db = fixture.CreateDbContext())
        {
            var store = new EfQuickResearchStore(db);
            await store.CreateAsync(run, CancellationToken.None);
            var claim = await store.ClaimAsync("writer", TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(run.Id, claim!.RunId);
            Assert.True(await store.CompleteAsync(claim, result, CancellationToken.None));
            Assert.False(await store.CompleteAsync(claim, result, CancellationToken.None));
        }
        await using var fresh = fixture.CreateDbContext();
        var read = new EfQuickResearchStore(fresh);
        var saved = await read.FindAsync(run.Id, owner, CancellationToken.None);
        Assert.Equal("Completed", saved!.Status);
        Assert.Equal(source, Assert.Single(saved.Result!.Sources));
        Assert.Equal(source.Id, Assert.Single(Assert.Single(saved.Result.Facts).SourceIds));
        Assert.Null(await read.FindAsync(run.Id, "other-owner", CancellationToken.None));
        Assert.Single((await read.ListAsync(owner, 1, 20, CancellationToken.None)).Items);
        Assert.Equal(0, (await read.ListAsync("other-owner", 1, 20, CancellationToken.None)).TotalCount);
    }

    [SkippableFact]
    public async Task ConcurrentClaims_AreExclusive_AndOldWorkerCannotWriteAfterReclaim()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var run = new QuickResearchRun("Synthetic concurrent question", "owner", DateTimeOffset.UtcNow);
        await new EfQuickResearchStore(db).CreateAsync(run, CancellationToken.None);
        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(async index =>
        {
            await using var connection = fixture.CreateDbContext();
            return await new EfQuickResearchStore(connection).ClaimAsync($"worker-{index}", TimeSpan.FromMinutes(1), CancellationToken.None);
        }));
        var old = Assert.Single(claims, item => item is not null)!;
        Assert.Equal(run.Id, old.RunId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {run.Id}");
        var store = new EfQuickResearchStore(db);
        var current = await store.ClaimAsync("new-worker", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(current!.LeaseVersion > old.LeaseVersion);
        Assert.False(await store.CompleteAsync(old, QuickResearchResult.CreateSynthetic(run.Id), CancellationToken.None));
        Assert.False(await store.FailAsync(old, CancellationToken.None));
        Assert.True(await store.CompleteAsync(current, QuickResearchResult.CreateSynthetic(run.Id), CancellationToken.None));
        Assert.Null(await store.ClaimAsync("later-worker", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [SkippableFact]
    public async Task FakeProcessor_DoesNotClaimOrAdvanceDeepResearch()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var question = new ResearchQuestion("Deep must remain queued", DateTimeOffset.UtcNow, "owner");
        var deep = new ResearchRun(question.Id, question.CreatedAt);
        db.ResearchQuestions.Add(question);
        db.ResearchRuns.Add(deep);
        await db.SaveChangesAsync();
        var store = new EfQuickResearchStore(db);
        var quick = new QuickResearchRun("Synthetic only", "owner", DateTimeOffset.UtcNow);
        await store.CreateAsync(quick, CancellationToken.None);
        Assert.True(await new QuickResearchProcessor(store, NullLogger<QuickResearchProcessor>.Instance).ProcessNextAsync("q0", TimeSpan.FromMinutes(1), CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(ResearchRunStatus.Queued, (await db.ResearchRuns.SingleAsync(x => x.Id == deep.Id)).Status);
        Assert.False(await db.ResearchPlans.AnyAsync(x => x.ResearchRunId == deep.Id));
        var result = await store.FindAsync(quick.Id, "owner", CancellationToken.None);
        Assert.True(result!.Result!.IsSynthetic);
        Assert.Empty(result.Result.Facts);
        Assert.Empty(result.Result.Sources);
    }

    [SkippableFact]
    public async Task FailedRun_HasSafeError_ClearedLease_AndIsNotReclaimed()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var run = new QuickResearchRun("Synthetic failure", "owner", DateTimeOffset.UtcNow);
        var store = new EfQuickResearchStore(db);
        await store.CreateAsync(run, CancellationToken.None);
        var claim = await store.ClaimAsync("failed-worker", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(await store.FailAsync(claim!, CancellationToken.None));
        var failed = await store.FindAsync(run.Id, "owner", CancellationToken.None);
        Assert.Equal("Failed", failed!.Status);
        Assert.Equal("QuickProcessingFailed", failed.SafeError);
        Assert.Null(failed.Result);
        Assert.Null(await store.ClaimAsync("later-worker", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [SkippableFact]
    public async Task PostgreSql_RejectsCompletedResultWithoutRunIdentity()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var run = new QuickResearchRun("Synthetic invalid payload", "owner", DateTimeOffset.UtcNow);
        await new EfQuickResearchStore(db).CreateAsync(run, CancellationToken.None);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET status = 'Completed', completed_at = clock_timestamp(), result = '{{}}'::jsonb WHERE id = {run.Id}"));
        // Remove only this isolated test's queued row, so it cannot interfere with later claim tests.
        await db.QuickResearchRuns.Where(x => x.Id == run.Id).ExecuteDeleteAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class QuickResearchPostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "Isolated Quick research PostgreSQL tests";
}

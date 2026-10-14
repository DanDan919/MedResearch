using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

[Collection(QuickResearchPostgreSqlCollection.Name)]
public sealed class QuickRetrievalPersistenceTests(PostgreSqlFixture fixture)
{
    private void RequireDatabase() => Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");

    [SkippableFact]
    public async Task Retrieval_PersistsSourcesAndBothProviderPaths_FromFreshContext()
    {
        RequireDatabase();
        var owner = Guid.NewGuid().ToString();
        var run = new QuickResearchRun("Magnesium insomnia adults", owner, DateTimeOffset.UtcNow);
        await using (var db = fixture.CreateDbContext())
        {
            var store = new EfQuickResearchStore(db);
            await store.CreateAsync(run, Guid.NewGuid(), CancellationToken.None);
            var claim = (await store.ClaimAsync("retrieval", TimeSpan.FromMinutes(1), CancellationToken.None))!;
            var sources = new TestSources("normal") { OnSearch = async attemptId =>
            {
                await using var fresh = fixture.CreateDbContext();
                var recorded = await fresh.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
                Assert.Contains(recorded.Retrieval!.Attempts, a => a.Id == attemptId && a.Outcome == QuickSearchOutcome.Started);
            } };
            await Processor(store, sources).ProcessAsync(claim, CancellationToken.None);
        }
        await using var readContext = fixture.CreateDbContext();
        var read = new EfQuickResearchStore(readContext);
        var result = (await read.FindAsync(run.Id, owner, CancellationToken.None))!;
        Assert.Equal("Completed", result.Status);
        Assert.Null(result.Result);
        Assert.Equal("Selected", result.Retrieval!.Outcome);
        Assert.Equal(5, result.Retrieval.Sources.Length);
        Assert.All(result.Retrieval.Sources, s => { Assert.Equal(run.Id, s.QuickRunId); Assert.Equal(2, s.Discoveries.Length); Assert.Null(s.Abstract); });
        Assert.All(result.Retrieval.Attempts, a => Assert.NotNull(a.ExecutedQuery));
        Assert.Null(await read.FindAsync(run.Id, "another-owner", CancellationToken.None));
        Assert.Single((await read.ListAsync(owner, 1, 20, CancellationToken.None)).Items);
        Assert.DoesNotContain("candidates", JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Empty((await readContext.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id)).Retrieval!.Candidates);
    }

    [SkippableTheory]
    [InlineData("zero", "Completed", "ZeroResults")]
    [InlineData("partial", "Completed", "Partial")]
    [InlineData("failed", "Failed", "Failed")]
    public async Task ProviderOutcomes_RemainDistinctAfterReload(string mode, string status, string outcome)
    {
        RequireDatabase();
        var run = new QuickResearchRun("Magnesium insomnia adults", "owner", DateTimeOffset.UtcNow);
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        await store.CreateAsync(run, Guid.NewGuid(), CancellationToken.None);
        var claim = (await store.ClaimAsync("worker", TimeSpan.FromMinutes(1), CancellationToken.None))!;
        await Processor(store, new TestSources(mode)).ProcessAsync(claim, CancellationToken.None);
        await using var fresh = fixture.CreateDbContext();
        var result = (await new EfQuickResearchStore(fresh).FindAsync(run.Id, "owner", CancellationToken.None))!;
        Assert.Equal(status, result.Status);
        Assert.Equal(outcome, result.Retrieval!.Outcome);
        Assert.Equal(status == "Failed" ? "QuickRetrievalFailed" : null, result.SafeError);
        Assert.Null(await new EfQuickResearchStore(fresh).ClaimAsync("later", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [SkippableFact]
    public async Task Reclaim_FencesOldSnapshotWrites_AndDoesNotRepeatAnUncertainProviderCall()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var run = new QuickResearchRun("Magnesium insomnia adults", "owner", DateTimeOffset.UtcNow);
        await store.CreateAsync(run, Guid.NewGuid(), CancellationToken.None);
        var old = (await store.ClaimAsync("old", TimeSpan.FromMinutes(1), CancellationToken.None))!;
        var plan = QuickQueryPreparation.Prepare(run.Question);
        var started = new QuickSearchAttempt(Guid.NewGuid(), run.Id, "PubMed", plan.Queries[0], plan.Queries[0], DateTimeOffset.UtcNow,
            null, QuickSearchOutcome.Started, null, null);
        var snapshot = new QuickRetrievalSnapshot(run.Id, plan, [started], [], [], QuickRetrievalOutcome.Searching, false, 0, []);
        Assert.True(await store.SaveRetrievalAsync(old, snapshot, false, CancellationToken.None));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {run.Id}");
        var current = (await store.ClaimAsync("new", TimeSpan.FromMinutes(1), CancellationToken.None))!;
        Assert.True(current.LeaseVersion > old.LeaseVersion);
        Assert.False(await store.SaveRetrievalAsync(old, snapshot, false, CancellationToken.None));
        var sources = new TestSources("normal");
        await Processor(store, sources).ProcessAsync(current, CancellationToken.None);
        Assert.Equal("EuropePmc", Assert.Single(sources.Calls));
        var result = (await store.FindAsync(run.Id, "owner", CancellationToken.None))!;
        Assert.Equal("Partial", result.Retrieval!.Outcome);
        Assert.Contains(result.Retrieval.Attempts, a => a.Outcome == "OutcomeUnknown");
    }

    [SkippableFact]
    public async Task DatabaseRejectsWrongRoot_AndReadGuardRejectsWrongRunSource()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var run = new QuickResearchRun("Magnesium insomnia adults", "owner", DateTimeOffset.UtcNow);
        await store.CreateAsync(run, Guid.NewGuid(), CancellationToken.None);
        var claim = (await store.ClaimAsync("reader", TimeSpan.FromMinutes(1), CancellationToken.None))!;
        var invalidRoot = JsonSerializer.Serialize(new { QuickRunId = Guid.NewGuid(), IsFinal = false });
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET retrieval = CAST({invalidRoot} AS jsonb) WHERE id = {run.Id}"));
        await Processor(store, new TestSources("normal")).ProcessAsync(claim, CancellationToken.None);
        var valid = (await db.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id)).Retrieval!;
        var foreignSource = valid with { Sources = [valid.Sources[0] with { QuickRunId = Guid.NewGuid() }] };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveRetrievalAsync(claim, foreignSource, true, CancellationToken.None));
        // Direct SQL is outside the write protocol; demonstrate that embedded lineage still fails closed on read.
        var forged = JsonSerializer.Serialize(foreignSource);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET retrieval = CAST({forged} AS jsonb) WHERE id = {run.Id}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.FindAsync(run.Id, "owner", CancellationToken.None));
        Assert.Null(await store.FindAsync(run.Id, "another-owner", CancellationToken.None));
    }

    private static QuickRetrievalProcessor Processor(EfQuickResearchStore store, TestSources sources) => new(store, sources, new QuickPublicationSelector(),
        new QuickRetrievalOptions { Enabled = true }, NullLogger<QuickRetrievalProcessor>.Instance);
    private sealed class TestSources(string mode) : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public List<string> Calls { get; } = [];
        public Func<Guid, Task>? OnSearch { get; init; }
        public string PrepareQuery(string provider, string query) => query;
        public async Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token)
        {
            Calls.Add(provider);
            if (OnSearch is not null) await OnSearch(attemptId);
            if (mode == "failed" || mode == "partial" && provider == "PubMed")
                throw new ScientificLiteratureSourceException("No raw message is stored", LiteratureProviderFailureCategory.NetworkFailure);
            return mode == "zero" ? new(0, [], 0) : new(8, Enumerable.Range(1, 8).Select(i => new QuickCandidate(i.ToString(), null, $"10.1234/p{i}",
                "Magnesium for insomnia in adults.", null, null, null, 2024, [], [], new(attemptId, provider, i.ToString()))).ToArray(), 0);
        }
    }
}

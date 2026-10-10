using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.Persistence;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderAttemptPersistenceTests(PostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task PreparedQuerySurvivesFreshContextAndRecoveryWithoutDuplicateDiscoveries()
    {
        Available();
        var seed = await Seed();
        var source = new FakeSource("EuropePmc", false, withResults: true) { PreparedQuery = "TITLE_ABS:(sleep)" };
        await using (var context = fixture.CreateDbContext())
        {
            var coordinator = new ScientificLiteratureSearchCoordinator([source], await Store(context, seed), NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
            await coordinator.SearchAsync(seed.RunId, seed.PlanId, ["sleep[tiab]"], CancellationToken.None);
        }
        await using (var recovered = fixture.CreateDbContext())
        {
            var coordinator = new ScientificLiteratureSearchCoordinator([source], await Store(recovered, seed), NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
            await coordinator.SearchAsync(seed.RunId, seed.PlanId, ["sleep[tiab]"], CancellationToken.None);
        }
        Assert.Equal(1, source.Calls);
        await using var verify = fixture.CreateDbContext();
        var attempt = await verify.LiteratureProviderAttempts.SingleAsync(x => x.ResearchRunId == seed.RunId);
        var search = await verify.LiteratureSearches.SingleAsync(x => x.ResearchRunId == seed.RunId);
        Assert.Equal("TITLE_ABS:(sleep)", attempt.Query);
        Assert.Equal(attempt.Query, search.Query);
        Assert.Equal(LiteratureProviderAttemptStatus.SucceededWithResults, attempt.Status);
        Assert.Equal(search.Id, attempt.LiteratureSearchId);
        Assert.Equal(1, await verify.ResearchStudyDiscoveries.CountAsync(x => x.ResearchRunId == seed.RunId));
    }

    [SkippableTheory]
    [InlineData(LiteratureProviderFailureCategory.NetworkFailure, LiteratureProviderAttemptStatus.Failed)]
    [InlineData(LiteratureProviderFailureCategory.Timeout, LiteratureProviderAttemptStatus.TimedOut)]
    [InlineData(LiteratureProviderFailureCategory.InvalidResponse, LiteratureProviderAttemptStatus.Failed)]
    [InlineData(LiteratureProviderFailureCategory.ResponseTooLarge, LiteratureProviderAttemptStatus.Failed)]
    [InlineData(LiteratureProviderFailureCategory.Cancelled, LiteratureProviderAttemptStatus.Cancelled)]
    public async Task FailedOutcomeSurvivesFreshContextWithoutScientificSearch(LiteratureProviderFailureCategory category, LiteratureProviderAttemptStatus status)
    {
        Available();
        var seed = await Seed();
        var id = Guid.NewGuid();
        await using (var context = fixture.CreateDbContext())
        {
            var store = await Store(context, seed);
            await store.BeginAttemptAsync(id, seed.RunId, seed.PlanId, "EuropePmc", "query", DateTimeOffset.UtcNow, CancellationToken.None);
            await store.FailAttemptAsync(id, category, DateTimeOffset.UtcNow, CancellationToken.None);
        }
        await using var verify = fixture.CreateDbContext();
        var attempt = await verify.LiteratureProviderAttempts.SingleAsync(x => x.Id == id);
        Assert.Equal(status, attempt.Status);
        Assert.Equal(category, attempt.FailureCategory);
        Assert.Null(attempt.ResultCount);
        Assert.Null(attempt.LiteratureSearchId);
        Assert.NotNull(attempt.CompletedAt);
        Assert.False(await verify.LiteratureSearches.AnyAsync(x => x.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task PartialFailureAndSuccessRemainOwnerAndRunScopedAndRecoveryReusesSuccess()
    {
        Available();
        var seed = await Seed();
        var other = await Seed();
        var pubMed = new FakeSource("PubMed", false, withResults: true);
        var europePmc = new FakeSource("EuropePmc", true);
        await using (var context = fixture.CreateDbContext())
        {
            var coordinator = new ScientificLiteratureSearchCoordinator([pubMed, europePmc], await Store(context, seed), NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
            await coordinator.SearchAsync(seed.RunId, seed.PlanId, ["query"], CancellationToken.None);
            await coordinator.SearchAsync(seed.RunId, seed.PlanId, ["query"], CancellationToken.None);
        }
        Assert.Equal(1, pubMed.Calls);
        Assert.Equal(2, europePmc.Calls);
        await using var verify = fixture.CreateDbContext();
        var model = await new EfResearchProvenanceStore(verify).FindAsync(seed.RunId, "attempt-owner", CancellationToken.None);
        Assert.NotNull(model);
        Assert.True(model.Coverage.HasPersistedProviderFailureProvenance);
        Assert.Equal(3, model.ProviderAttempts.Count);
        Assert.Single(model.ProviderAttempts, x => x.Status == LiteratureProviderAttemptStatus.SucceededWithResults);
        Assert.Single(model.Searches);
        Assert.Single(model.Studies);
        Assert.Null(await new EfResearchProvenanceStore(verify).FindAsync(seed.RunId, "other-owner", CancellationToken.None));
        Assert.Empty((await new EfResearchProvenanceStore(verify).FindAsync(other.RunId, "attempt-owner", CancellationToken.None))!.ProviderAttempts);
    }

    [SkippableFact]
    public async Task AllProviderFailuresPersistBeforeStageFails()
    {
        Available();
        var seed = await Seed();
        await using var context = fixture.CreateDbContext();
        var coordinator = new ScientificLiteratureSearchCoordinator([new FakeSource("PubMed", true), new FakeSource("EuropePmc", true)], await Store(context, seed), NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() => coordinator.SearchAsync(seed.RunId, seed.PlanId, ["query"], CancellationToken.None));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(2, await verify.LiteratureProviderAttempts.CountAsync(x => x.ResearchRunId == seed.RunId && x.Status == LiteratureProviderAttemptStatus.Failed));
        Assert.False(await verify.LiteratureSearches.AnyAsync(x => x.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task HostCancellationPersistsCancelledWithoutScientificFailure()
    {
        Available();
        var seed = await Seed();
        using var cancel = new CancellationTokenSource();
        await using var context = fixture.CreateDbContext();
        var coordinator = new ScientificLiteratureSearchCoordinator([new FakeSource("PubMed", false, cancel)], await Store(context, seed), NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.SearchAsync(seed.RunId, seed.PlanId, ["query"], cancel.Token));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(LiteratureProviderAttemptStatus.Cancelled, (await verify.LiteratureProviderAttempts.SingleAsync(x => x.ResearchRunId == seed.RunId)).Status);
        Assert.Equal(ResearchRunStatus.Planning, (await verify.ResearchRuns.SingleAsync(x => x.Id == seed.RunId)).Status);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OldOwnerCannotPersistLateSuccessOrFailureAfterReclaim(bool success)
    {
        Available();
        var seed = await Seed();
        var id = Guid.NewGuid();
        await using var old = fixture.CreateDbContext();
        var oldStore = await Store(old, seed);
        await oldStore.BeginAttemptAsync(id, seed.RunId, seed.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, CancellationToken.None);
        await using (var takeover = fixture.CreateDbContext())
        {
            var run = await takeover.ResearchRuns.SingleAsync(x => x.Id == seed.RunId);
            run.AssignLease("worker-b", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), 2);
            await takeover.SaveChangesAsync();
        }
        if (success)
            await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => oldStore.PersistSearchResultsAsync(new ScientificSearchPersistenceRequest(id, seed.RunId, seed.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, 0, []), CancellationToken.None));
        else
            await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => oldStore.FailAttemptAsync(id, LiteratureProviderFailureCategory.NetworkFailure, DateTimeOffset.UtcNow, CancellationToken.None));
        await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => oldStore.BeginAttemptAsync(Guid.NewGuid(), seed.RunId, seed.PlanId, "PubMed", "new query", DateTimeOffset.UtcNow, CancellationToken.None));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(LiteratureProviderAttemptStatus.Started, (await verify.LiteratureProviderAttempts.SingleAsync(x => x.Id == id)).Status);
        Assert.False(await verify.LiteratureSearches.AnyAsync(x => x.ResearchRunId == seed.RunId));
        // New owner resumes without inventing the abandoned call's final outcome.
        var runB = await verify.ResearchRuns.AsNoTracking().SingleAsync(x => x.Id == seed.RunId);
        var fenceB = new PostgreSqlResearchRunWriteFence(verify);
        fenceB.Attach(new ClaimedResearchRun(runB, "question", "worker-b", 2, DateTimeOffset.UtcNow.AddMinutes(5), true));
        var storeB = new EfScientificSearchResultStore(verify, NullLogger<EfScientificSearchResultStore>.Instance, fenceB);
        var newId = Guid.NewGuid();
        await storeB.BeginAttemptAsync(newId, seed.RunId, seed.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, CancellationToken.None);
        await storeB.PersistSearchResultsAsync(new ScientificSearchPersistenceRequest(newId, seed.RunId, seed.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, 0, []), CancellationToken.None);
        Assert.Equal(1, await verify.LiteratureProviderAttempts.CountAsync(x => x.ResearchRunId == seed.RunId && x.Status == LiteratureProviderAttemptStatus.SucceededZeroResults));
    }

    [SkippableFact]
    public async Task AttemptRejectsPlanFromOtherRun()
    {
        Available();
        var a = await Seed();
        var b = await Seed();
        await using var context = fixture.CreateDbContext();
        var store = await Store(context, a);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.BeginAttemptAsync(Guid.NewGuid(), a.RunId, b.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [SkippableFact]
    public async Task ConcurrentFinishedAttemptsCannotOverwriteOutcome()
    {
        Available();
        var seed = await Seed();
        var id = Guid.NewGuid();
        await using (var context = fixture.CreateDbContext())
            await (await Store(context, seed)).BeginAttemptAsync(id, seed.RunId, seed.PlanId, "PubMed", "query", DateTimeOffset.UtcNow, CancellationToken.None);
        await using var a = fixture.CreateDbContext();
        await using var b = fixture.CreateDbContext();
        // Preload both snapshots deliberately; the status concurrency token fences double completion even without worker DI.
        await a.LiteratureProviderAttempts.SingleAsync(x => x.Id == id);
        await b.LiteratureProviderAttempts.SingleAsync(x => x.Id == id);
        await new EfScientificSearchResultStore(a).FailAttemptAsync(id, LiteratureProviderFailureCategory.Timeout, DateTimeOffset.UtcNow, CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfScientificSearchResultStore(b).FailAttemptAsync(id, LiteratureProviderFailureCategory.NetworkFailure, DateTimeOffset.UtcNow, CancellationToken.None));
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(LiteratureProviderAttemptStatus.TimedOut, (await verify.LiteratureProviderAttempts.SingleAsync(x => x.Id == id)).Status);
    }

    private async Task<SeedState> Seed()
    {
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("question", now, "attempt-owner");
        var run = new ResearchRun(question.Id, now);
        run.StartPlanning(now);
        run.AssignLease("worker-a", now, now.AddMinutes(5), 1);
        var plan = new ResearchPlan(Guid.NewGuid(), run.Id, question.Id, question.Text, null, null, null, [], [], ["query"], [], "fake", "fake", "fixture", now);
        await using var context = fixture.CreateDbContext();
        context.AddRange(question, run, plan);
        await context.SaveChangesAsync();
        return new SeedState(run.Id, plan.Id);
    }
    private static async Task<EfScientificSearchResultStore> Store(MedResearchDbContext context, SeedState seed)
    {
        var run = await context.ResearchRuns.AsNoTracking().SingleAsync(x => x.Id == seed.RunId);
        var fence = new PostgreSqlResearchRunWriteFence(context);
        fence.Attach(new ClaimedResearchRun(run, "question", "worker-a", 1, DateTimeOffset.UtcNow.AddMinutes(5), false));
        return new EfScientificSearchResultStore(context, NullLogger<EfScientificSearchResultStore>.Instance, fence);
    }
    private void Available() => Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {fixture.UnavailableReason}");
    private sealed record SeedState(Guid RunId, Guid PlanId);
    private sealed class FakeSource(string source, bool fail, CancellationTokenSource? cancel = null, bool withResults = false) : IScientificLiteratureSource
    {
        public string SourceName => source;
        public int Calls { get; private set; }
        public string? PreparedQuery { get; init; }
        public string PrepareQuery(string query) => PreparedQuery ?? query;
        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken token)
        {
            Calls++;
            cancel?.Cancel();
            token.ThrowIfCancellationRequested();
            if (fail) throw new ScientificLiteratureSourceException("bounded", LiteratureProviderFailureCategory.NetworkFailure);
            ScientificStudyCandidate[] candidates = withResults
                ? [new ScientificStudyCandidate(null, null, $"10.9999/{request.ResearchRunId:N}", "Fixture publication", "Abstract", null, null, 1998, null, null, [], [], "fixture", source)]
                : [];
            return Task.FromResult(new ScientificSearchResult(source, DateTimeOffset.UtcNow, candidates.Length, candidates));
        }
    }
}

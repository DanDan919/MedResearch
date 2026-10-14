using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace MedResearch.IntegrationTests;

[Collection(QuickResearchPostgreSqlCollection.Name)]
public sealed class QuickCreationIdempotencyTests(PostgreSqlFixture fixture)
{
    private void RequireDatabase() => Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");

    [SkippableFact]
    public async Task ConcurrentIdenticalPosts_OnSeparateApiHosts_CreateExactlyOneRun()
    {
        RequireDatabase();
        var owner = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            using var a = new QuickResearchApiTests.QuickApiFactory(fixture);
            using var b = new QuickResearchApiTests.QuickApiFactory(fixture);
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
            {
                using var client = (index % 2 == 0 ? a : b).ForOwner(owner, key);
                using var response = await client.PostAsJsonAsync("/api/quick-research", new { question = "  A private test question  " });
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                Assert.True(response.Headers.CacheControl!.Private); Assert.True(response.Headers.CacheControl.NoStore);
                return (await response.Content.ReadFromJsonAsync<QuickResearchDetails>())!;
            }));
            Assert.Single(results.Select(result => result.Id).Distinct());
            Assert.All(results, result => Assert.Equal("A private test question", result.Question));
            await using var db = fixture.CreateDbContext();
            var saved = await db.QuickResearchRuns.Where(run => run.OwnerSubjectId == owner)
                .Select(run => new { run.Id, Key = EF.Property<Guid?>(run, "CreationKey"), Hash = EF.Property<string>(run, "CreationFingerprint") }).SingleAsync();
            Assert.Equal(key, saved.Key); Assert.Equal(results[0].Id, saved.Id);
            Assert.Equal(QuickCreateIdentity.Fingerprint("A private test question"), saved.Hash);
        }
        finally { await DeleteOwned(owner); }
    }

    [SkippableFact]
    public async Task ConflictDoesNotCreateOrphan_AndSameKeyForDifferentOwnerRemainsIsolated()
    {
        RequireDatabase();
        var owner = Guid.NewGuid().ToString(); var other = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            using var host = new QuickResearchApiTests.QuickApiFactory(fixture);
            using var a = host.ForOwner(owner, key); using var b = host.ForOwner(other, key);
            var first = await Post(a, "Private question A"); var second = await Post(b, "Private question B");
            Assert.NotEqual(first.Id, second.Id);
            using var conflict = await a.PostAsJsonAsync("/api/quick-research", new { question = "Different private question" });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Contains("admission-idempotency-conflict", await conflict.Content.ReadAsStringAsync());
            Assert.DoesNotContain("Private question A", await conflict.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/quick-research/{first.Id}")).StatusCode);
            Assert.Equal(1, (await a.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
            using var newSubmission = host.ForOwner(owner, Guid.NewGuid());
            Assert.NotEqual(first.Id, (await Post(newSubmission, "Private question A")).Id);
            Assert.Equal(2, (await a.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
            Assert.Equal(1, (await b.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
        }
        finally { await DeleteOwned(owner, other); }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayAfterTerminalState_ReturnsCurrentStateAndSameRun(bool failed)
    {
        RequireDatabase(); var owner = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            Guid id;
            using (var host = new QuickResearchApiTests.QuickApiFactory(fixture))
            {
                using var client = host.ForOwner(owner, key); id = (await Post(client, "Terminal replay question")).Id;
            }
            await using (var db = fixture.CreateDbContext())
            {
                var store = new EfQuickResearchStore(db);
                var claim = await store.ClaimAsync("q41-terminal", TimeSpan.FromMinutes(1), default);
                Assert.Equal(id, claim!.RunId);
                Assert.True(failed ? await store.FailAsync(claim, default)
                    : await store.CompleteAsync(claim, QuickResearchResult.CreateSynthetic(id), default));
            }
            using var restarted = new QuickResearchApiTests.QuickApiFactory(fixture);
            using var replay = restarted.ForOwner(owner, key);
            var result = await Post(replay, "Terminal replay question");
            Assert.Equal(id, result.Id); Assert.Equal(failed ? "Failed" : "Completed", result.Status);
            Assert.NotNull(result.CompletedAt);
            Assert.Equal(1, (await replay.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
        }
        finally { await DeleteOwned(owner); }
    }

    [SkippableFact]
    public async Task ResponseLostAfterActualCommit_ReplayOnRestartDoesNotCreateSecondRun()
    {
        RequireDatabase(); var owner = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            using (var host = new QuickResearchApiTests.QuickApiFactory(fixture))
            {
                using var client = host.CreateDefaultClient(new LoseSuccessfulResponse());
                client.DefaultRequestHeaders.Add("X-Q0-Test-Subject", owner);
                client.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
                await Assert.ThrowsAsync<IOException>(() => client.PostAsJsonAsync("/api/quick-research", new { question = "Lost response question" }));
            }
            await using var db = fixture.CreateDbContext();
            var persisted = await db.QuickResearchRuns.AsNoTracking().SingleAsync(run => run.OwnerSubjectId == owner);
            using var restarted = new QuickResearchApiTests.QuickApiFactory(fixture);
            using var replay = restarted.ForOwner(owner, key);
            Assert.Equal(persisted.Id, (await Post(replay, "Lost response question")).Id);
            Assert.Equal(1, (await replay.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
        }
        finally { await DeleteOwned(owner); }
    }

    [SkippableFact]
    public async Task ActualDatabaseErrorBeforeCommit_RollsBackRunAndKey_ThenSameKeyCanSucceed()
    {
        RequireDatabase(); var owner = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            var options = new DbContextOptionsBuilder<MedResearchDbContext>(fixture.DbContextOptions).AddInterceptors(new FailCommit()).Options;
            await using (var failed = new MedResearchDbContext(options))
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => new EfQuickResearchStore(failed)
                    .CreateAsync(new("Rollback question", owner, DateTimeOffset.UtcNow), key, default));
                Assert.Equal("22012", exception.SqlState);
            }
            await using var fresh = fixture.CreateDbContext();
            Assert.False(await fresh.QuickResearchRuns.AnyAsync(run => run.OwnerSubjectId == owner));
            var result = await new EfQuickResearchStore(fresh).CreateAsync(new("Rollback question", owner, DateTimeOffset.UtcNow), key, default);
            Assert.Equal("Queued", result.Status);
            Assert.Equal(1, await fresh.QuickResearchRuns.CountAsync(run => run.OwnerSubjectId == owner));
        }
        finally { await DeleteOwned(owner); }
    }

    [SkippableFact]
    public async Task DatabaseUniqueConstraintIsFinalAuthority_NotOnlyTheApi()
    {
        RequireDatabase(); var owner = Guid.NewGuid().ToString(); var key = Guid.NewGuid();
        try
        {
            await using var db = fixture.CreateDbContext();
            await new EfQuickResearchStore(db).CreateAsync(new("Constraint question", owner, DateTimeOffset.UtcNow), key, default);
            var duplicate = new QuickResearchRun("Constraint question", owner, DateTimeOffset.UtcNow);
            db.QuickResearchRuns.Add(duplicate);
            db.Entry(duplicate).Property("CreationKey").CurrentValue = key;
            db.Entry(duplicate).Property("CreationFingerprint").CurrentValue = QuickCreateIdentity.Fingerprint(duplicate.Question);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal("23505", postgres.SqlState); Assert.Equal("ux_quick_owner_creation_key", postgres.ConstraintName);
        }
        finally { await DeleteOwned(owner); }
    }

    private async Task DeleteOwned(params string[] owners)
    {
        await using var db = fixture.CreateDbContext();
        await db.QuickResearchRuns.Where(run => owners.Contains(run.OwnerSubjectId)).ExecuteDeleteAsync();
    }
    private static async Task<QuickResearchDetails> Post(HttpClient client, string question)
    {
        using var response = await client.PostAsJsonAsync("/api/quick-research", new { question });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuickResearchDetails>())!;
    }
    private sealed class LoseSuccessfulResponse : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var committed = await base.SendAsync(request, token);
            Assert.Equal(HttpStatusCode.Accepted, committed.StatusCode);
            throw new IOException("Injected lost response after the real API/database commit.");
        }
    }
    private sealed class FailCommit : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken token = default)
        {
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction; command.CommandText = "SELECT 1 / 0";
            await command.ExecuteNonQueryAsync(token);
            return result;
        }
    }
}

using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

[Collection(QuickResearchPostgreSqlCollection.Name)]
public sealed class QuickAnswerPersistenceTests(PostgreSqlFixture fixture)
{
    private void RequireDatabase() => Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");

    [SkippableFact]
    public async Task Answer_ReloadsWithSourcesAndOwnerIsolation_AndTerminalLeaseCleared()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var (run, claim) = await Seed(store);
        var llm = new Llm(claim) { BeforeCall = async () =>
        {
            await using var fresh = fixture.CreateDbContext();
            var saved = await fresh.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
            Assert.Equal(QuickAnswerOutcome.Started, saved.AnswerGeneration!.Outcome);
            Assert.Equal(1, saved.AnswerGeneration.AttemptCount);
            Assert.True(saved.Retrieval!.IsFinal);
            Assert.Equal(QuickResearchStatus.Processing, saved.Status);
        } };
        await Processor(store, llm).ProcessAsync(claim, default);
        await using var read = fixture.CreateDbContext();
        var savedDetails = (await new EfQuickResearchStore(read).FindAsync(run.Id, run.OwnerSubjectId, default))!;
        Assert.Equal("Completed", savedDetails.Status);
        Assert.Equal("Succeeded", savedDetails.AnswerStatus);
        Assert.Equal(claim.Retrieval!.Sources[0].Id, Assert.Single(savedDetails.Answer!.SourceReferences));
        Assert.Equal(2, savedDetails.Retrieval!.Sources.Length);
        Assert.Null(savedDetails.Result);
        Assert.Null(await new EfQuickResearchStore(read).FindAsync(run.Id, "foreign", default));
        Assert.Empty((await new EfQuickResearchStore(read).ListAsync("foreign", 1, 20, default)).Items);
        var root = await read.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        Assert.Null(root.LeaseOwner);
        Assert.Null(root.LeaseExpiresAt);
        Assert.Equal(1, llm.Calls);
    }

    [SkippableFact]
    public async Task ConcurrentReservations_AllowOnlyOneDispatchBudget()
    {
        RequireDatabase();
        await using var seed = fixture.CreateDbContext();
        var (run, claim) = await Seed(new(seed));
        var pending = Pending(claim);
        await using var a = fixture.CreateDbContext();
        await using var b = fixture.CreateDbContext();
        var results = await Task.WhenAll(new EfQuickResearchStore(a).ReserveAnswerAsync(claim, pending, default),
            new EfQuickResearchStore(b).ReserveAnswerAsync(claim, pending with { Id = Guid.NewGuid() }, default));
        Assert.Equal(1, results.Count(x => x));
        var root = await seed.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        Assert.True(await new EfQuickResearchStore(seed).SettleAnswerAsync(claim,
            root.AnswerGeneration! with { Outcome = QuickAnswerOutcome.OutcomeUnknown, FailureCategory = "OutcomeUnknown", FinishedAt = DateTimeOffset.UtcNow }, default));
    }

    [SkippableFact]
    public async Task CrashAfterReservation_ReclaimFailsUnknown_AndOldWorkerCannotWrite()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var (run, old) = await Seed(store);
        var pending = Pending(old);
        Assert.True(await store.ReserveAnswerAsync(old, pending, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {run.Id}");
        var current = (await store.ClaimAsync("new", TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(run.Id, current.RunId);
        Assert.True(current.LeaseVersion > old.LeaseVersion);
        var stale = pending with { Outcome = QuickAnswerOutcome.Succeeded, FinishedAt = DateTimeOffset.UtcNow,
            Answer = QuickAnswerValidator.ValidateDraft(Draft(old), old.Retrieval!, old.Retrieval!.Sources.Select(s => s.Id).ToArray()) };
        Assert.False(await store.SettleAnswerAsync(old, stale, default));
        Assert.False(await store.FailAsync(old, default));
        var llm = new Llm(current);
        await Processor(store, llm).ProcessAsync(current, default);
        var result = (await store.FindAsync(run.Id, run.OwnerSubjectId, default))!;
        Assert.Equal("Failed", result.Status);
        Assert.Equal("OutcomeUnknown", result.AnswerStatus);
        Assert.Equal("QuickAnswerOutcomeUnknown", result.SafeError);
        Assert.Null(result.Answer);
        Assert.Equal(0, llm.Calls);
        Assert.Null(await store.ClaimAsync("again", TimeSpan.FromMinutes(2), default));
    }

    [SkippableTheory]
    [InlineData("invalid")]
    [InlineData("timeout")]
    [InlineData("metadata")]
    public async Task FailureAndInsufficiency_DoNotRerunRetrieval_OrPersistInvalidAnswer(string mode)
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var (run, claim) = await Seed(store, metadataOnly: mode == "metadata");
        var before = JsonSerializer.Serialize(claim.Retrieval);
        var llm = new Llm(claim, mode);
        await Processor(store, llm).ProcessAsync(claim, default);
        await using var fresh = fixture.CreateDbContext();
        var root = await fresh.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        Assert.Equal(before, JsonSerializer.Serialize(root.Retrieval));
        Assert.Equal(mode == "metadata" ? QuickResearchStatus.Completed : QuickResearchStatus.Failed, root.Status);
        Assert.Equal(mode == "metadata" ? 0 : 1, root.AnswerGeneration!.AttemptCount);
        if (mode == "metadata") Assert.Equal("Insufficient", root.AnswerGeneration.Answer!.Uncertainty);
        else Assert.Null(root.AnswerGeneration.Answer);
        Assert.Equal(mode == "metadata" ? 0 : 1, llm.Calls);
    }

    [SkippableFact]
    public async Task SavedInputDrift_FencesReceipt_AndStillDoesNotAllowRedispatch()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var (run, claim) = await Seed(store);
        var pending = Pending(claim);
        Assert.True(await store.ReserveAnswerAsync(claim, pending, default));
        var changed = claim.Retrieval! with { Sources = claim.Retrieval!.Sources.Select(s => s with { Abstract = s.Abstract + " Extra source context." }).ToArray() };
        var json = JsonSerializer.Serialize(changed);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET retrieval = CAST({json} AS jsonb) WHERE id = {run.Id}");
        Assert.False(await store.SettleAnswerAsync(claim, pending with { Outcome = QuickAnswerOutcome.Succeeded, FinishedAt = DateTimeOffset.UtcNow,
            Answer = QuickAnswerValidator.ValidateDraft(Draft(claim), claim.Retrieval, claim.Retrieval.Sources.Select(s => s.Id).ToArray()) }, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {run.Id}");
        var recovered = (await store.ClaimAsync("recovery", TimeSpan.FromMinutes(2), default))!;
        var llm = new Llm(recovered);
        await Processor(store, llm).ProcessAsync(recovered, default);
        Assert.Equal(0, llm.Calls);
    }

    [SkippableFact]
    public async Task WrongRunAnswers_AreRejectedByStoreRootCheckAndReadGuard()
    {
        RequireDatabase();
        await using var db = fixture.CreateDbContext();
        var store = new EfQuickResearchStore(db);
        var (run, claim) = await Seed(store);
        var pending = Pending(claim);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReserveAnswerAsync(claim, pending with { QuickRunId = Guid.NewGuid() }, default));
        var forgedRoot = JsonSerializer.Serialize(pending with { QuickRunId = Guid.NewGuid() });
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET answer_generation = CAST({forgedRoot} AS jsonb) WHERE id = {run.Id}"));
        await Processor(store, new Llm(claim)).ProcessAsync(claim, default);
        var saved = (await db.QuickResearchRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id)).AnswerGeneration!;
        var foreignId = Guid.NewGuid();
        var answer = saved.Answer!;
        var forged = saved with { Answer = answer with { Findings = [answer.Findings[0] with { Citations = [answer.Findings[0].Citations[0] with { SourceId = foreignId }] }], SourceReferences = [foreignId] } };
        var json = JsonSerializer.Serialize(forged);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE quick_research_runs SET answer_generation = CAST({json} AS jsonb) WHERE id = {run.Id}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.FindAsync(run.Id, run.OwnerSubjectId, default));
        Assert.Null(await store.FindAsync(run.Id, "foreign-owner", default));
    }

    private static QuickAnswerProcessor Processor(EfQuickResearchStore store, Llm llm) => new(store, llm,
        new() { Enabled = true }, NullLogger<QuickAnswerProcessor>.Instance);
    private static QuickAnswerGeneration Pending(QuickResearchClaim claim) => new(Guid.NewGuid(), claim.RunId,
        QuickAnswerPrompt.Version, QuickAnswerPrompt.Build(claim, new()).Fingerprint, 1, DateTimeOffset.UtcNow, null, QuickAnswerOutcome.Started, null, null);

    private static async Task<(QuickResearchRun, QuickResearchClaim)> Seed(EfQuickResearchStore store, bool metadataOnly = false)
    {
        var run = new QuickResearchRun("Magnesium insomnia adults", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        await store.CreateAsync(run, Guid.NewGuid(), default);
        var claim = (await store.ClaimAsync("answer-worker", TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(run.Id, claim.RunId);
        var retrieval = await new QuickRetrievalProcessor(store, new Sources(metadataOnly), new QuickPublicationSelector(),
            new() { Enabled = true }, NullLogger<QuickRetrievalProcessor>.Instance).ProcessAsync(claim, default, settle: false);
        return (run, claim with { Retrieval = retrieval });
    }
    private static QuickAnswerDraft Draft(QuickResearchClaim claim)
    {
        var source = claim.Retrieval!.Sources[0];
        const string text = "The abstract describes sleep outcomes in adults.";
        return new(text, [new(text, [new(source.Id, source.Abstract!)])], [], ["The available description is limited."], "VeryLimited", [source.Id]);
    }
    private sealed class Sources(bool metadataOnly) : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public string PrepareQuery(string provider, string query) => query;
        public Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token)
        {
            var pmid = provider == "PubMed" ? "123" : "456";
            return Task.FromResult(new QuickProviderPage(1, [new(pmid, null, null, "Magnesium insomnia in adults",
                metadataOnly ? null : "This abstract describes sleep outcomes with magnesium in adults. Details are limited.",
                null, null, null, [], [], new(attemptId, provider, pmid))], 0));
        }
    }
    private sealed class Llm(QuickResearchClaim claim, string mode = "normal") : IStructuredLlmClient
    {
        public int Calls { get; private set; }
        public Func<Task>? BeforeCall { get; init; }
        public async Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken token)
        {
            Calls++;
            if (BeforeCall is not null) await BeforeCall();
            if (mode == "timeout") throw new StructuredLlmTimeoutException();
            var draft = Draft(claim);
            if (mode == "invalid") draft = draft with { SourceReferences = [Guid.NewGuid()] };
            return new((T)(object)draft, new("Fake", "fake-answer", null, DateTimeOffset.UtcNow));
        }
    }
}

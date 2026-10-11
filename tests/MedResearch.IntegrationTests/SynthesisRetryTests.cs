using System.Reflection;
using MedResearch.Application.DependencyInjection;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Application.Security;
using MedResearch.Domain;
using MedResearch.Infrastructure.DependencyInjection;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research.Processing;
using MedResearch.Infrastructure.Synthesis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

[Collection(SynthesisRetryPostgreSqlCollection.Name)]
public sealed class SynthesisRetryTests(PostgreSqlFixture fixture)
{
    private readonly PostgreSqlFixture _fixture = fixture;
    private const string RetryOwner = ResearchOwnership.LegacyUnownedSubjectId;

    private void SkipIfPostgreSqlUnavailable() => Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {_fixture.UnavailableReason}");
    private static Task<ResearchSynthesisResult> CreateStructuredResultAsync(MedResearchDbContext db, Guid runId) => ResearchReportStoreTests.CreateStructuredResultAsync(db, runId);

    [SkippableTheory]
    [InlineData(ResearchRunStatus.Planning)]
    [InlineData(ResearchRunStatus.Searching)]
    [InlineData(ResearchRunStatus.Extracting)]
    [InlineData(ResearchRunStatus.Evaluating)]
    [InlineData(ResearchRunStatus.Synthesizing)]
    public async Task SynthesisRetry_OnlyTrustedSynthesisFailureIsEligible(ResearchRunStatus stage)
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync(stage);
        await using var db = _fixture.CreateDbContext();
        var store = RetryStore(db);
        Assert.Equal(stage == ResearchRunStatus.Synthesizing ? "Available" : "Unavailable",
            (await store.GetAsync(runId, RetryOwner, CancellationToken.None))!.Availability);
        if (stage != ResearchRunStatus.Synthesizing)
            await Assert.ThrowsAsync<SynthesisRetryConflictException>(() => store.AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None));
    }

    [SkippableTheory]
    [InlineData("legacy")]
    [InlineData("missing-plan")]
    [InlineData("cross-run-evaluation")]
    [InlineData("corrupt-source")]
    [InlineData("unsupported-quote")]
    [InlineData("missing-evaluation")]
    [InlineData("cross-run-extraction")]
    [InlineData("report")]
    public async Task SynthesisRetry_FailsClosedForUnknownOrInvalidInputs(string defect)
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        await using var db = _fixture.CreateDbContext();
        if (defect == "legacy") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_runs SET failed_stage = NULL, failure_category = NULL WHERE id = {runId}");
        if (defect == "missing-plan") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE literature_searches SET research_plan_id = NULL WHERE research_run_id = {runId}");
        if (defect == "cross-run-evaluation") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE evidence_evaluations SET evidence_ids = ARRAY[{Guid.NewGuid()}]::uuid[] WHERE research_run_id = {runId}");
        if (defect == "corrupt-source") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE source_materials SET content_hash = 'incorrect' WHERE id IN (SELECT source_material_id FROM evidence_extractions WHERE research_run_id = {runId})");
        if (defect == "unsupported-quote") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE evidence SET supporting_text = 'Fabricated source quotation' WHERE research_run_id = {runId}");
        if (defect == "missing-evaluation") await db.EvidenceEvaluations.Where(x => x.ResearchRunId == runId).ExecuteDeleteAsync();
        if (defect == "cross-run-extraction")
        {
            var other = await new ResearchReportStoreTests(_fixture).SeedRunWithEvidenceAsync(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE evidence SET evidence_extraction_id = {(await db.EvidenceExtractions.Where(x => x.ResearchRunId == other.RunId).Select(x => x.Id).SingleAsync())} WHERE research_run_id = {runId}");
            // The other isolated test run must not be admitted to the ordinary queue.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_runs SET status = 'Cancelled' WHERE id = {other.RunId}");
        }
        if (defect == "report") await new EfResearchSynthesisStore(db).PersistReportAsync(await CreateStructuredResultAsync(db, runId), CancellationToken.None);
        Assert.Equal("Unavailable", (await RetryStore(db).GetAsync(runId, RetryOwner, CancellationToken.None))!.Availability);
        await Assert.ThrowsAsync<SynthesisRetryConflictException>(() => RetryStore(db).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await db.SynthesisRetryAttempts.AnyAsync(x => x.ResearchRunId == runId));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynthesisRetry_TenConcurrentAdmissions_CommitExactlyOne(bool sameKey)
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        var key = Guid.NewGuid();
        var requests = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = _fixture.CreateDbContext();
            try { return await RetryStore(db).AdmitAsync(runId, RetryOwner, sameKey ? key : Guid.NewGuid(), CancellationToken.None); }
            catch (SynthesisRetryConflictException) { return null; }
        }).ToArray();
        var responses = await Task.WhenAll(requests);
        Assert.Equal(sameKey ? 10 : 1, responses.Count(x => x is not null));
        Assert.Single(responses.Where(x => x is not null).Select(x => x!.RetryAttemptId).Distinct());
        await using var fresh = _fixture.CreateDbContext();
        Assert.Single(await fresh.SynthesisRetryAttempts.Where(x => x.ResearchRunId == runId).ToArrayAsync());
        Assert.Equal(ResearchRunStatus.Failed, (await fresh.ResearchRuns.SingleAsync(x => x.Id == runId)).Status);
        Assert.Null(await RetryStore(fresh).GetAsync(runId, "other-owner", CancellationToken.None));
        Assert.Null(await RetryStore(fresh).AdmitAsync(runId, "other-owner", key, CancellationToken.None));
        // Cleanup only this fixture's admitted run; no production connection is used.
        await fresh.Database.ExecuteSqlInterpolatedAsync($"UPDATE synthesis_retry_attempts SET status = 'Failed' WHERE research_run_id = {runId}");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynthesisRetry_TenStudyRecovery_OnlySynthesisCalls_OneReport(bool repair)
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync(tenStudies: true);
        await using var before = _fixture.CreateDbContext();
        var context = await new SynthesisContextBuilder(new EfResearchSynthesisStore(before), new(), NullLogger<SynthesisContextBuilder>.Instance).BuildAsync(runId, CancellationToken.None);
        Assert.Equal(10, context.Studies.Count);
        Assert.Equal(29, context.Statistics.IncludedEvidenceFindingCount);
        var claims = context.Studies.SelectMany(x => x.Evidence).Take(12).Select((item, index) =>
            new ResearchReportClaimDraft(index == 0 ? "Conclusion" : "Finding", "Positive", null, [item.EvidenceId.ToString()],
                Kind: "QualitativeEffect", Outcome: item.Outcome, Population: item.Population,
                ExposureOrIntervention: item.ExposureOrIntervention, Comparator: item.Comparator, Timepoint: item.Timepoint)).ToArray();
        var valid = new ResearchReportDraft("Completed", null, null, null, null, null, null, "VeryLimited", claims);
        var fake = new RetryLlm(repair ? [valid with { Claims = [.. claims, claims[0]] }, valid] : [valid]);
        var countsBefore = await PriorArtifactCountsAsync(before, runId);
        var key = Guid.NewGuid();
        await RetryStore(before).AdmitAsync(runId, RetryOwner, key, CancellationToken.None);
        using var provider = RetryProvider(fake);
        using (var scope = provider.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>().ProcessNextQueuedRunAsync("retry-e2e", CancellationToken.None));
        await using var fresh = _fixture.CreateDbContext();
        Assert.Equal(ResearchRunStatus.Completed, (await fresh.ResearchRuns.SingleAsync(x => x.Id == runId)).Status);
        var report = await new EfResearchSynthesisStore(fresh).FindReportAsync(runId, RetryOwner, CancellationToken.None);
        Assert.Equal(12, report!.Claims.Count);
        Assert.All(report.Claims, claim => Assert.Equal(ResearchClaimGroundingStatus.StructuredValidated, claim.GroundingStatus));
        Assert.Null(await new EfResearchSynthesisStore(fresh).FindReportAsync(runId, "other-owner", CancellationToken.None));
        Assert.Equal(countsBefore, await PriorArtifactCountsAsync(fresh, runId));
        Assert.Equal(repair ? 2 : 1, fake.Calls);
        Assert.Equal(0, provider.GetRequiredService<PreviousStageCalls>().Count);
        var state = await RetryStore(fresh).GetAsync(runId, RetryOwner, CancellationToken.None);
        Assert.Equal("Succeeded", state!.AttemptStatus);
        Assert.Equal(fake.Calls, state.GenerationCallsReserved);
        Assert.Equal(state.RetryAttemptId, (await RetryStore(fresh).AdmitAsync(runId, RetryOwner, key, CancellationToken.None))!.RetryAttemptId);
        await Assert.ThrowsAsync<SynthesisRetryConflictException>(() => RetryStore(fresh).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None));
        Assert.Single(await fresh.ResearchReports.Where(x => x.ResearchRunId == runId).ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynthesisRetry_CrashReclaim_FencesOldOwner_AndNeverReplaysPendingCall(bool dispatched)
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        await using var firstDb = _fixture.CreateDbContext();
        await RetryStore(firstDb).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None);
        var first = await new PostgreSqlResearchRunQueue(firstDb).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "owner-A", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(runId, first!.Run.Id);
        Assert.Equal(ResearchRunStatus.Synthesizing, first.Run.Status);
        var oldFence = new PostgreSqlResearchRunWriteFence(firstDb);
        oldFence.Attach(first);
        var old = RetryStore(firstDb, oldFence);
        if (dispatched) await old.ReserveAsync(runId, CancellationToken.None);
        var result = await CreateStructuredResultAsync(firstDb, runId);
        await firstDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_runs SET processing_lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {runId}");
        await using var secondDb = _fixture.CreateDbContext();
        var second = await new PostgreSqlResearchRunQueue(secondDb).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "owner-B", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(runId, second!.Run.Id);
        Assert.True(second.LeaseVersion > first.LeaseVersion);
        var newFence = new PostgreSqlResearchRunWriteFence(secondDb);
        newFence.Attach(second);
        var current = RetryStore(secondDb, newFence);
        await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => old.RecordResponseAsync(runId, CancellationToken.None));
        await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => new EfResearchSynthesisStore(firstDb, oldFence).PersistReportAsync(result, CancellationToken.None));
        Assert.False(await new PostgreSqlResearchRunQueue(firstDb).MarkFailedAsync(first, "Safe failure", DateTimeOffset.UtcNow, CancellationToken.None));
        if (dispatched)
        {
            var unknown = await Assert.ThrowsAsync<SynthesisRetryBudgetException>(() => current.ValidateInputsAsync(runId, CancellationToken.None));
            Assert.True(unknown.OutcomeUnknown);
            Assert.True(await new PostgreSqlResearchRunQueue(secondDb).MarkFailedAsync(second, unknown.Message, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.Equal("OutcomeUnknown", (await current.GetAsync(runId, RetryOwner, CancellationToken.None))!.AttemptStatus);
        }
        else
        {
            await current.ReserveAsync(runId, CancellationToken.None);
            await current.RecordResponseAsync(runId, CancellationToken.None);
            var reports = new EfResearchSynthesisStore(secondDb, newFence, retryInputs: new SynthesisRetryInputs(secondDb, new(), new FixedEffectQuantitativeStatisticalSynthesizer(new())));
            await reports.PersistReportAsync(result, CancellationToken.None);
            await reports.PersistReportAsync(result, CancellationToken.None);
            second.Run.Complete(DateTimeOffset.UtcNow);
            Assert.True(await new PostgreSqlResearchRunQueue(secondDb).SaveProgressAsync(second, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None));
            Assert.Equal("Succeeded", (await current.GetAsync(runId, RetryOwner, CancellationToken.None))!.AttemptStatus);
        }
    }

    [SkippableFact]
    public async Task SynthesisRetry_CrashAfterAcceptedReport_CompletesWithoutGeneration()
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        await using (var db = _fixture.CreateDbContext())
        {
            await RetryStore(db).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None);
            var claim = await new PostgreSqlResearchRunQueue(db).TryClaimNextQueuedRunAsync(
                DateTimeOffset.UtcNow, "report-writer", TimeSpan.FromMinutes(5), CancellationToken.None);
            Assert.Equal(runId, claim!.Run.Id);
            var fence = new PostgreSqlResearchRunWriteFence(db);
            fence.Attach(claim);
            var budget = RetryStore(db, fence);
            await budget.ReserveAsync(runId, CancellationToken.None);
            await budget.RecordResponseAsync(runId, CancellationToken.None);
            var reports = new EfResearchSynthesisStore(db, fence, retryInputs: new SynthesisRetryInputs(db, new(), new FixedEffectQuantitativeStatisticalSynthesizer(new())));
            await reports.PersistReportAsync(await CreateStructuredResultAsync(db, runId), CancellationToken.None);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_runs SET processing_lease_expires_at = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE id = {runId}");
        }

        var fake = new RetryLlm([]);
        using var provider = RetryProvider(fake);
        using (var scope = provider.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>().ProcessNextQueuedRunAsync("report-completer", CancellationToken.None));
        await using var fresh = _fixture.CreateDbContext();
        Assert.Equal(ResearchRunStatus.Completed, (await fresh.ResearchRuns.SingleAsync(x => x.Id == runId)).Status);
        Assert.Single(await fresh.ResearchReports.Where(x => x.ResearchRunId == runId).ToArrayAsync());
        var state = await RetryStore(fresh).GetAsync(runId, RetryOwner, CancellationToken.None);
        Assert.Equal("Succeeded", state!.AttemptStatus);
        Assert.Equal(1, state.GenerationCallsReserved);
        Assert.Equal(0, fake.Calls);
        Assert.Equal(0, provider.GetRequiredService<PreviousStageCalls>().Count);
        Assert.Null(await new PostgreSqlResearchRunQueue(fresh).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "no-more-work", TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [SkippableFact]
    public async Task SynthesisRetry_CompetingWorkers_OnlyOneClaim_AndBoundedReservations()
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        await using (var db = _fixture.CreateDbContext()) await RetryStore(db).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None);
        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(async index =>
        {
            await using var db = _fixture.CreateDbContext();
            return await new PostgreSqlResearchRunQueue(db).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, $"worker-{index}", TimeSpan.FromMinutes(5), CancellationToken.None);
        }));
        var claim = Assert.Single(claims, x => x is not null)!;
        Assert.Equal(runId, claim.Run.Id);
        await using var fresh = _fixture.CreateDbContext();
        var fence = new PostgreSqlResearchRunWriteFence(fresh);
        fence.Attach(claim);
        var store = RetryStore(fresh, fence);
        for (var index = 0; index < 2; index++)
        {
            await store.ReserveAsync(runId, CancellationToken.None);
            await store.RecordResponseAsync(runId, CancellationToken.None);
        }
        Assert.False((await Assert.ThrowsAsync<SynthesisRetryBudgetException>(() => store.ReserveAsync(runId, CancellationToken.None))).OutcomeUnknown);
        Assert.Equal(2, (await store.GetAsync(runId, RetryOwner, CancellationToken.None))!.GenerationCallsReserved);
        Assert.True(await new PostgreSqlResearchRunQueue(fresh).MarkFailedAsync(claim, "The report generation attempt budget is exhausted.", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [SkippableFact]
    public async Task SynthesisRetry_ChangedInputsAfterAdmission_BlockBeforeDispatch()
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        await using var db = _fixture.CreateDbContext();
        await RetryStore(db).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None);
        var claim = await new PostgreSqlResearchRunQueue(db).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "input-check", TimeSpan.FromMinutes(5), CancellationToken.None);
        var fence = new PostgreSqlResearchRunWriteFence(db);
        fence.Attach(claim!);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE evidence SET result_summary = 'Changed after admission' WHERE research_run_id = {runId}");
        await Assert.ThrowsAsync<SynthesisRetryConflictException>(() => RetryStore(db, fence).ReserveAsync(runId, CancellationToken.None));
        Assert.Equal(0, (await db.SynthesisRetryAttempts.AsNoTracking().SingleAsync(x => x.ResearchRunId == runId)).GenerationCallsReserved);
        await new PostgreSqlResearchRunQueue(db).MarkFailedAsync(claim!, "Safe failure", DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task<Guid> SeedRetryAsync(ResearchRunStatus stage = ResearchRunStatus.Synthesizing, bool tenStudies = false)
    {
        var seeds = new ResearchReportStoreTests(_fixture);
        var runId = tenStudies ? await seeds.SeedTenStudyRunAsync() : (await seeds.SeedRunWithEvidenceAsync(1)).RunId;
        await using var db = _fixture.CreateDbContext();
        // Initial synthesis already produced its deterministic artifacts before failing.
        await new SynthesisContextBuilder(new EfResearchSynthesisStore(db), new(), NullLogger<SynthesisContextBuilder>.Instance,
            quantitativeSynthesisArtifactStore: new EfQuantitativeSynthesisArtifactStore(db)).BuildAsync(runId, CancellationToken.None);
        var run = await db.ResearchRuns.SingleAsync(x => x.Id == runId);
        var now = DateTimeOffset.UtcNow;
        run.StartPlanning(now);
        if (stage != ResearchRunStatus.Planning) run.StartSearching(now);
        if (stage is ResearchRunStatus.Extracting or ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing) run.StartExtraction(now);
        if (stage is ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing) run.StartEvaluation(now);
        if (stage == ResearchRunStatus.Synthesizing) run.StartSynthesis(now);
        run.Fail(ResearchProcessingFailures.ReportClaimLimitMessage, now);
        await db.SaveChangesAsync();
        return runId;
    }

    [SkippableFact]
    public async Task SynthesisRetry_ProviderTimeout_IsUnknown_AndCannotCreateAnotherAttempt()
    {
        SkipIfPostgreSqlUnavailable();
        var runId = await SeedRetryAsync();
        var key = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext()) await RetryStore(db).AdmitAsync(runId, RetryOwner, key, CancellationToken.None);
        var fake = new RetryLlm([], failTransport: true);
        using var provider = RetryProvider(fake);
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>().ProcessNextQueuedRunAsync("timeout-worker", CancellationToken.None);
        await using var fresh = _fixture.CreateDbContext();
        var state = await RetryStore(fresh).GetAsync(runId, RetryOwner, CancellationToken.None);
        Assert.Equal("OutcomeUnknown", state!.AttemptStatus);
        Assert.Equal("ProviderOutcomeUnknown", state.FailureCategory);
        Assert.Equal(1, state.GenerationCallsReserved);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(0, provider.GetRequiredService<PreviousStageCalls>().Count);
        Assert.Equal(ResearchRunStatus.Failed, (await fresh.ResearchRuns.SingleAsync(x => x.Id == runId)).Status);
        Assert.Equal(state, await RetryStore(fresh).AdmitAsync(runId, RetryOwner, key, CancellationToken.None));
        await Assert.ThrowsAsync<SynthesisRetryConflictException>(() => RetryStore(fresh).AdmitAsync(runId, RetryOwner, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await new PostgreSqlResearchRunQueue(fresh).TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "no-replay", TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    private static EfSynthesisRetryStore RetryStore(MedResearchDbContext db, IResearchRunWriteFence? fence = null) => new(db,
        new SynthesisRetryInputs(db, new(), new FixedEffectQuantitativeStatisticalSynthesizer(new())), new(), fence ?? new PostgreSqlResearchRunWriteFence(db));

    private static async Task<int[]> PriorArtifactCountsAsync(MedResearchDbContext db, Guid runId) =>
    [await db.ResearchPlans.CountAsync(x => x.ResearchRunId == runId), await db.LiteratureSearches.CountAsync(x => x.ResearchRunId == runId),
        await db.ResearchStudyDiscoveries.CountAsync(x => x.ResearchRunId == runId), await db.EvidenceExtractions.CountAsync(x => x.ResearchRunId == runId),
        await db.Evidence.CountAsync(x => x.ResearchRunId == runId), await db.EvidenceEvaluations.CountAsync(x => x.ResearchRunId == runId),
        await db.SourceMaterials.CountAsync(), await db.QuantitativeSynthesisArtifacts.CountAsync(x => x.ResearchRunId == runId)];

    private ServiceProvider RetryProvider(RetryLlm fake)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MedResearch"] = _fixture.ConnectionString,
            ["ResearchProcessing:Enabled"] = "false", ["Database:ApplyMigrationsOnStartup"] = "false"
        }).Build());
        services.AddScoped<IStructuredLlmClient>(_ => fake);
        var calls = new PreviousStageCalls();
        services.AddSingleton(calls);
        services.AddScoped<IResearchPlanner>(_ => PreviousStageGuard.Create<IResearchPlanner>(calls));
        services.AddScoped<IScientificLiteratureSearchCoordinator>(_ => PreviousStageGuard.Create<IScientificLiteratureSearchCoordinator>(calls));
        services.AddScoped<ISourceMaterialAcquirer>(_ => PreviousStageGuard.Create<ISourceMaterialAcquirer>(calls));
        services.AddScoped<IEvidenceExtractor>(_ => PreviousStageGuard.Create<IEvidenceExtractor>(calls));
        services.AddScoped<IEvidenceEvaluator>(_ => PreviousStageGuard.Create<IEvidenceEvaluator>(calls));
        return services.BuildServiceProvider();
    }

    private sealed class RetryLlm(ResearchReportDraft[] drafts, bool failTransport = false) : IStructuredLlmClient
    {
        public int Calls { get; private set; }
        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(typeof(ResearchReportDraft), typeof(T));
            if (failTransport)
            {
                Calls++;
                return Task.FromException<StructuredGenerationResult<T>>(new TimeoutException("Synthetic provider timeout"));
            }
            Assert.True(Calls < drafts.Length);
            return Task.FromResult(new StructuredGenerationResult<T>((T)(object)drafts[Calls++], new("FakeLLM", "deterministic", null, DateTimeOffset.UtcNow)));
        }
    }

    public sealed class PreviousStageCalls { public int Count; }
    public class PreviousStageGuard : DispatchProxy
    {
        private PreviousStageCalls _calls = null!;
        public static T Create<T>(PreviousStageCalls calls) where T : class
        {
            var proxy = Create<T, PreviousStageGuard>();
            ((PreviousStageGuard)(object)proxy)._calls = calls;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref _calls.Count);
            throw new InvalidOperationException("A previous scientific stage must not run during synthesis retry.");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SynthesisRetryPostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "Isolated synthesis retry PostgreSQL tests";
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using MedResearch.Api.Research;
using MedResearch.Application.Research.Admission;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Trait("Category", "PostgreSql")]
public sealed class ResearchAdmissionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [SkippableFact]
    public async Task SameOwner_TenParallelCreates_AdmitOneWithoutOrphans()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(index => CreateAsync(client, Guid.NewGuid(), $"Bounded research question {index}")));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ResearchRuns.CountAsync());
        Assert.Equal(1, await db.ResearchQuestions.CountAsync());
        Assert.Equal(1, await db.ResearchAdmissions.CountAsync());
    }

    [SkippableFact]
    public async Task MultipleApiInstances_ParallelOwners_AdmitTwoGlobally()
    {
        await ResetAsync();
        using var first = new JwtApiFactory(fixture.ConnectionString);
        using var second = new JwtApiFactory(fixture.ConnectionString);
        using var a = Client(first, "owner-a");
        using var b = Client(second, "owner-b");
        using var c = Client(first, "owner-c");
        using var d = Client(second, "owner-d");
        var responses = await Task.WhenAll(new[] { a, b, c, d }
            .Select(client => CreateAsync(client, Guid.NewGuid(), "Global capacity research question")));

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.ResearchRuns.CountAsync());
        Assert.Equal(2, await db.ResearchQuestions.CountAsync());
        Assert.Equal(2, await db.ResearchAdmissions.CountAsync());
    }

    [SkippableFact]
    public async Task LostCreateResponse_RetrySameOwnerKeyBody_ReturnsSameRun()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        var key = Guid.NewGuid();
        using var first = await CreateAsync(client, key, "Idempotent scientific question");
        using var retry = await CreateAsync(client, key, "Idempotent scientific question");
        var original = await first.Content.ReadFromJsonAsync<CreateResearchResponse>();
        var replay = await retry.Content.ReadFromJsonAsync<CreateResearchResponse>();

        Assert.NotNull(original);
        Assert.NotNull(replay);
        Assert.Equal(original.ResearchRunId, replay.ResearchRunId);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ResearchRuns.CountAsync());
        Assert.Equal(1, await db.ResearchQuestions.CountAsync());
    }

    private async Task ResetAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE research_questions CASCADE");
    }

    [SkippableFact]
    public async Task SeparateApiProcesses_SameOwner_AtomicCapAndReplay()
    {
        await ResetAsync();
        await using var first = await ApiProcess.StartAsync(fixture.ConnectionString!, "process-owner");
        await using var second = await ApiProcess.StartAsync(fixture.ConnectionString!, "process-owner");
        Assert.NotEqual(first.Id, second.Id);
        var keys = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var responses = await Task.WhenAll(keys.Select((key, index) => CreateAsync(index % 2 == 0 ? first.Client : second.Client, key, "Cross-process research question")));
        var accepted = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        var acceptedKey = keys[Array.IndexOf(responses, accepted)];
        var replays = await Task.WhenAll(Enumerable.Range(0, 10).Select(index => CreateAsync(index % 2 == 0 ? first.Client : second.Client,
            acceptedKey, "Cross-process research question")));
        Assert.All(replays, response => { Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(accepted.Headers.Location, response.Headers.Location); });
        await AssertCountsAsync(1);
    }

    [SkippableFact]
    public async Task SeparateApiProcesses_DifferentOwners_AtomicGlobalCap()
    {
        await ResetAsync();
        await using var a = await ApiProcess.StartAsync(fixture.ConnectionString!, "process-a");
        await using var b = await ApiProcess.StartAsync(fixture.ConnectionString!, "process-b");
        await using var c = await ApiProcess.StartAsync(fixture.ConnectionString!, "process-c");
        var responses = await Task.WhenAll(new[] { a.Client, b.Client, c.Client }.Select(client =>
            CreateAsync(client, Guid.NewGuid(), "Cross-process global capacity")));
        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
        await AssertCountsAsync(2);
    }

    [SkippableFact]
    public async Task SameKey_TenParallelRetriesAcrossInstances_CreateOneReservation()
    {
        await ResetAsync();
        using var first = new JwtApiFactory(fixture.ConnectionString);
        using var second = new JwtApiFactory(fixture.ConnectionString);
        using var a = Client(first, "owner");
        using var b = Client(second, "owner");
        var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(index => CreateAsync(index % 2 == 0 ? a : b, key, "Same canonical research question")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<CreateResearchResponse>()));
        Assert.Single(bodies.Select(body => body!.ResearchRunId).Distinct());
        await AssertCountsAsync(1);
    }

    [SkippableFact]
    public async Task OwnerScopedKey_ConflictDoesNotLeakOrMutateAnotherOwner()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var a = Client(factory, "owner-a");
        using var b = Client(factory, "owner-b");
        b.DefaultRequestHeaders.Add("X-Owner-Id", "owner-a");
        var key = Guid.NewGuid();
        using var first = await CreateAsync(a, key, "Original canonical question");
        using var trimmedReplay = await CreateAsync(a, key, "  Original canonical question  ");
        Assert.Equal(first.Headers.Location, trimmedReplay.Headers.Location);
        using var conflict = await CreateAsync(a, key, "Different scientific question");
        await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "admission-idempotency-conflict");
        using var other = await CreateAsync(b, key, "Original canonical question");
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.NotEqual(first.Headers.Location, other.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(first.Headers.Location)).StatusCode);
        await AssertCountsAsync(2);
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef,01234567-89ab-cdef-0123-456789abcdef")]
    public async Task InvalidOrMissingKey_RejectsWithoutRows(string? key)
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/research")
        { Content = JsonContent.Create(new CreateResearchRequest("Invalid key scientific question")) };
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "admission-invalid-key");
        await AssertCountsAsync(0);
    }

    [SkippableFact]
    public async Task OperatorStop_AllowsReplayAndReads_ButNoNewWork()
    {
        await ResetAsync();
        using var active = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(active, "owner");
        var key = Guid.NewGuid();
        using var original = await CreateAsync(client, key, "Already committed research question");
        using var stopped = new JwtApiFactory(fixture.ConnectionString,
            admissionOptions: new ResearchAdmissionOptions { StopNewAdmissions = true });
        using var stoppedClient = Client(stopped, "owner");
        using var retry = await CreateAsync(stoppedClient, key, "Already committed research question");
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(original.Headers.Location, retry.Headers.Location);
        using var rejected = await CreateAsync(stoppedClient, Guid.NewGuid(), "New research while paused");
        await AssertErrorAsync(rejected, HttpStatusCode.ServiceUnavailable, "admission-stopped");
        Assert.Equal(HttpStatusCode.OK, (await stoppedClient.GetAsync(original.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stoppedClient.GetAsync("/api/research")).StatusCode);
        await AssertCountsAsync(1);
    }

    [SkippableTheory]
    [InlineData(ResearchRunStatus.Completed)]
    [InlineData(ResearchRunStatus.Failed)]
    [InlineData(ResearchRunStatus.Cancelled)]
    public async Task TerminalState_ReleasesOutstanding_NotDaily_UtcMidnightResets(ResearchRunStatus terminal)
    {
        await ResetAsync();
        var clock = new TestAdmissionClock(new DateTimeOffset(2030, 10, 10, 23, 59, 59, TimeSpan.Zero));
        using var factory = new JwtApiFactory(fixture.ConnectionString, admissionClock: clock);
        using var client = Client(factory, "owner");
        var key = Guid.NewGuid();
        for (var index = 0; index < 2; index++)
        {
            using var response = await CreateAsync(client, index == 0 ? key : Guid.NewGuid(), "Daily research question");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var run = (await response.Content.ReadFromJsonAsync<CreateResearchResponse>())!;
            await SetTerminalAsync(run.ResearchRunId, terminal);
        }
        using var denied = await CreateAsync(client, Guid.NewGuid(), "Third daily research question");
        await AssertErrorAsync(denied, HttpStatusCode.TooManyRequests, "admission-owner-daily");
        Assert.Equal("1", Assert.Single(denied.Headers.GetValues("Retry-After")));
        using var replay = await CreateAsync(client, key, "Daily research question");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        // Exact UTC midnight, with no wall-clock wait or process-local time zone dependence.
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        using var nextDay = await CreateAsync(client, Guid.NewGuid(), "New day research question");
        Assert.Equal(HttpStatusCode.Created, nextDay.StatusCode);
        await AssertCountsAsync(3);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ResearchAdmissions.CountAsync(row => row.CreatedAt >= clock.UtcNow));
    }

    [SkippableFact]
    public async Task GlobalDailyQuota_CountsTerminalAdmissionsAcrossOwners()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString,
            admissionOptions: new ResearchAdmissionOptions { GlobalDailyLimit = 2 });
        foreach (var owner in new[] { "owner-a", "owner-b" })
        {
            using var client = Client(factory, owner);
            using var response = await CreateAsync(client, Guid.NewGuid(), "Daily global research question");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await SetTerminalAsync((await response.Content.ReadFromJsonAsync<CreateResearchResponse>())!.ResearchRunId, ResearchRunStatus.Failed);
        }
        using var third = Client(factory, "owner-c");
        using var rejected = await CreateAsync(third, Guid.NewGuid(), "Over daily global capacity");
        await AssertErrorAsync(rejected, HttpStatusCode.TooManyRequests, "admission-global-daily");
        await AssertCountsAsync(2);
    }

    [SkippableTheory]
    [InlineData(ResearchRunStatus.Queued)]
    [InlineData(ResearchRunStatus.Planning)]
    [InlineData(ResearchRunStatus.Searching)]
    [InlineData(ResearchRunStatus.Extracting)]
    [InlineData(ResearchRunStatus.Evaluating)]
    [InlineData(ResearchRunStatus.Synthesizing)]
    public async Task EveryNonterminalState_EvenWithoutValidLease_ConsumesOutstanding(ResearchRunStatus status)
    {
        await ResetAsync();
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("Pre-migration outstanding question", now, "owner");
        var run = new ResearchRun(Guid.NewGuid(), question.Id, status, now, status == ResearchRunStatus.Queued ? null : now, null, null);
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(question, run);
            await db.SaveChangesAsync();
        }
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var response = await CreateAsync(client, Guid.NewGuid(), "Cannot bypass expired processing");
        await AssertErrorAsync(response, HttpStatusCode.TooManyRequests, "admission-owner-outstanding");
        await using var check = fixture.CreateDbContext();
        Assert.Equal(1, await check.ResearchRuns.CountAsync());
        Assert.Equal(0, await check.ResearchAdmissions.CountAsync());
    }

    [SkippableFact]
    public async Task PreMigrationTerminalRuns_CountTowardDailyQuota()
    {
        await ResetAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            for (var index = 0; index < 2; index++)
            {
                var question = new ResearchQuestion("Historical accepted research", now, "owner");
                db.AddRange(question, new ResearchRun(Guid.NewGuid(), question.Id, ResearchRunStatus.Failed, now, now, now, "Synthetic failure"));
            }
            await db.SaveChangesAsync();
        }
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var response = await CreateAsync(client, Guid.NewGuid(), "No quota reset on deployment");
        await AssertErrorAsync(response, HttpStatusCode.TooManyRequests, "admission-owner-daily");
    }

    [SkippableFact]
    public async Task ForeignKeyFailure_RollsBackReservationAndQuestion_KeyCanBeRetried()
    {
        await ResetAsync();
        var key = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            var store = new EfResearchStore(db);
            await Assert.ThrowsAsync<DbUpdateException>(() => store.PersistInitialResearchAsync(
                new ResearchQuestion("Atomic rollback question", now, "owner"), new ResearchRun(Guid.NewGuid(), now),
                "owner", key, CancellationToken.None));
        }
        await AssertCountsAsync(0);
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var retry = await CreateAsync(client, key, "Atomic rollback question");
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        await AssertCountsAsync(1);
    }

    [SkippableFact]
    public async Task CancellationBeforeCommit_LeavesNoReservationOrQuestion()
    {
        await ResetAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var db = fixture.CreateDbContext();
        var question = new ResearchQuestion("Cancelled admission", DateTimeOffset.UtcNow, "owner");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfResearchStore(db).PersistInitialResearchAsync(
            question, new ResearchRun(question.Id, question.CreatedAt), "owner", Guid.NewGuid(), cancellation.Token));
        await AssertCountsAsync(0);
    }

    private async Task SetTerminalAsync(Guid id, ResearchRunStatus terminal)
    {
        await using var db = fixture.CreateDbContext();
        var run = await db.ResearchRuns.SingleAsync(run => run.Id == id);
        if (terminal == ResearchRunStatus.Failed) run.Fail("Synthetic failure", DateTimeOffset.UtcNow);
        else if (terminal == ResearchRunStatus.Cancelled) run.Cancel(DateTimeOffset.UtcNow);
        else
        {
            run.StartPlanning(DateTimeOffset.UtcNow);
            run.StartSearching(DateTimeOffset.UtcNow);
            run.StartExtraction(DateTimeOffset.UtcNow);
            run.StartEvaluation(DateTimeOffset.UtcNow);
            run.StartSynthesis(DateTimeOffset.UtcNow);
            run.Complete(DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task ReservationConstraints_RejectDuplicateKeysDuplicateRunsAndOrphans()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        var key = Guid.NewGuid();
        using var accepted = await CreateAsync(client, key, "Constraint-backed admission");
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var runId = (await accepted.Content.ReadFromJsonAsync<CreateResearchResponse>())!.ResearchRunId;
        await using var originalDb = fixture.CreateDbContext();
        var questionId = (await originalDb.ResearchRuns.AsNoTracking().SingleAsync(run => run.Id == runId)).ResearchQuestionId;
        foreach (var row in new[]
        {
            new ResearchAdmissionEntity { OwnerSubjectId = "owner", IdempotencyKey = key, ResearchRunId = Guid.NewGuid() },
            new ResearchAdmissionEntity { OwnerSubjectId = "other-owner", IdempotencyKey = Guid.NewGuid(), ResearchRunId = runId },
            new ResearchAdmissionEntity { OwnerSubjectId = "other-owner", IdempotencyKey = Guid.NewGuid(), ResearchRunId = Guid.NewGuid() }
        })
        {
            row.RequestFingerprint = ResearchCreateIdentity.Fingerprint("Constraint test"); row.CreatedAt = DateTimeOffset.UtcNow;
            await using var db = fixture.CreateDbContext();
            if (row.IdempotencyKey == key) db.ResearchRuns.Add(new ResearchRun(row.ResearchRunId, questionId, ResearchRunStatus.Queued,
                DateTimeOffset.UtcNow, null, null, null));
            db.ResearchAdmissions.Add(row);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<Npgsql.PostgresException>(error.InnerException);
            Assert.Equal(row.IdempotencyKey == key ? "PK_research_admissions" : row.ResearchRunId == runId
                ? "IX_research_admissions_research_run_id" : "FK_research_admissions_research_runs_research_run_id", postgres.ConstraintName);
        }
        await using (var db = fixture.CreateDbContext())
        {
            db.Remove(await db.ResearchRuns.SingleAsync(run => run.Id == runId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await AssertCountsAsync(1);
    }

    [SkippableFact]
    public async Task CancellationInsideAdmissionTransaction_RollsBackAndReleasesLock()
    {
        await ResetAsync();
        using var cancellation = new CancellationTokenSource();
        await using (var db = fixture.CreateDbContext())
        {
            var question = new ResearchQuestion("Cancelled during admission", DateTimeOffset.UtcNow, "owner");
            var store = new EfResearchStore(db, new ResearchAdmissionOptions(), new CancellingClock(cancellation), NullLogger<EfResearchStore>.Instance);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PersistInitialResearchAsync(question,
                new ResearchRun(question.Id, question.CreatedAt), "owner", Guid.NewGuid(), cancellation.Token));
        }
        await AssertCountsAsync(0);
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var retry = await CreateAsync(client, Guid.NewGuid(), "Lock released after cancellation");
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        await AssertCountsAsync(1);
    }

    private sealed class CancellingClock(CancellationTokenSource cancellation) : IResearchAdmissionClock
    {
        public Task<DateTimeOffset> ReadUtcNowAsync(MedResearchDbContext db, CancellationToken cancellationToken)
        {
            cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not propagated.");
        }
    }

    private async Task AssertCountsAsync(int count)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal(count, await db.ResearchQuestions.CountAsync());
        Assert.Equal(count, await db.ResearchRuns.CountAsync());
        Assert.Equal(count, await db.ResearchAdmissions.CountAsync());
    }

    [SkippableFact]
    public async Task WorkerReclaimAndFencing_DoNotDuplicateAdmissionOrReleaseCapacity()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        using var created = await CreateAsync(client, Guid.NewGuid(), "Reclaim one accepted research run");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var now = DateTimeOffset.UtcNow;
        await using var firstDb = fixture.CreateDbContext();
        await using var secondDb = fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstDb);
        var secondQueue = new PostgreSqlResearchRunQueue(secondDb);
        var first = await firstQueue.TryClaimNextQueuedRunAsync(now, "worker-a", TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.NotNull(first);
        var second = await secondQueue.TryClaimNextQueuedRunAsync(now.AddSeconds(2), "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(first.Run.Id, second.Run.Id);
        Assert.False(await firstQueue.MarkFailedAsync(first, "Stale worker", now.AddSeconds(3), CancellationToken.None));
        using var rejected = await CreateAsync(client, Guid.NewGuid(), "Expired lease is not terminal");
        await AssertErrorAsync(rejected, HttpStatusCode.TooManyRequests, "admission-owner-outstanding");
        await AssertCountsAsync(1);
        Assert.True(await secondQueue.MarkFailedAsync(second, "Synthetic worker failure", now.AddSeconds(4), CancellationToken.None));
        using var next = await CreateAsync(client, Guid.NewGuid(), "New work after real terminal state");
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        await AssertCountsAsync(2);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("researchRunId", out _));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    private sealed class TestAdmissionClock(DateTimeOffset utcNow) : IResearchAdmissionClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public Task<DateTimeOffset> ReadUtcNowAsync(MedResearchDbContext db, CancellationToken cancellationToken) => Task.FromResult(UtcNow);
    }

    private sealed class ApiProcess(Process process, HttpClient client) : IAsyncDisposable
    {
        public int Id => process.Id;
        public HttpClient Client => client;

        public static async Task<ApiProcess> StartAsync(string connectionString, string owner)
        {
            var root = OpenApiContractTests.RepositoryRoot();
            var configuration = typeof(ResearchAdmissionTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(root, "src", "MedResearch.Api"), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(start.WorkingDirectory, "bin", configuration, "net10.0", "MedResearch.Api.dll"));
            // Explicit isolated developer identity. Production JWT isolation is verified separately.
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            start.Environment["ConnectionStrings__MedResearch"] = connectionString;
            start.Environment["Authentication__Mode"] = "DevelopmentLocal";
            start.Environment["Authentication__DevelopmentSubject"] = owner;
            start.Environment["Database__ApplyMigrationsOnStartup"] = "false";
            start.Environment["ResearchProcessing__Enabled"] = "false";
            start.Environment["AI__Provider"] = "OpenAI";
            start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            foreach (var pair in new Dictionary<string, string> { ["OwnerOutstandingLimit"] = "1", ["GlobalOutstandingLimit"] = "2",
                ["OwnerDailyLimit"] = "2", ["GlobalDailyLimit"] = "10", ["StopNewAdmissions"] = "false" })
                start.Environment["ResearchAdmission__" + pair.Key] = pair.Value;
            var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var child = new Process { StartInfo = start, EnableRaisingEvents = true };
            child.OutputDataReceived += (_, args) =>
            {
                const string prefix = "Now listening on: ";
                var line = args.Data?.Trim();
                if (line?.StartsWith(prefix, StringComparison.Ordinal) == true) listening.TrySetResult(line[prefix.Length..]);
            };
            child.ErrorDataReceived += (_, _) => { };
            child.Exited += (_, _) => listening.TrySetException(new InvalidOperationException("Isolated API exited before readiness."));
            try
            {
                child.Start(); child.BeginOutputReadLine(); child.BeginErrorReadLine();
                var url = await listening.Task.WaitAsync(TimeSpan.FromSeconds(60));
                var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
                return new ApiProcess(child, client);
            }
            catch
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose(); throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }

    private static HttpClient Client(JwtApiFactory factory, string owner)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", owner));
        return client;
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid key, string question)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/research")
        {
            Content = JsonContent.Create(new CreateResearchRequest(question))
        };
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        return await client.SendAsync(request);
    }
}

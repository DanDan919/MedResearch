using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchRunQueueConcurrencyTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly PostgreSqlFixture _fixture;

    public ResearchRunQueueConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_ClaimsQueuedRun()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Can queued research work be claimed durably?");
        var claimedAt = DateTimeOffset.UtcNow;

        await using var context = _fixture.CreateDbContext();
        var queue = new PostgreSqlResearchRunQueue(context);

        var claimedRun = await queue.TryClaimNextQueuedRunAsync(claimedAt, "worker-a", LeaseDuration, CancellationToken.None);

        Assert.NotNull(claimedRun);
        Assert.Equal(runId, claimedRun.Run.Id);
        Assert.Equal(ResearchRunStatus.Planning, claimedRun.Run.Status);
        Assert.Equal("worker-a", claimedRun.WorkerInstanceId);
        Assert.Equal("worker-a", claimedRun.Run.ProcessingLeaseOwner);
        Assert.Equal(1, claimedRun.LeaseVersion);
        Assert.False(claimedRun.WasReclaimed);
        Assert.NotNull(claimedRun.Run.StartedAt);
        Assert.True(claimedRun.LeaseExpiresAt > claimedAt);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_ClaimedRunIsNotClaimableAgainBeforeLeaseExpiry()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Can a claimed research run be claimed twice?");
        var now = DateTimeOffset.UtcNow;

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstContext);
        var secondQueue = new PostgreSqlResearchRunQueue(secondContext);

        var firstClaim = await firstQueue.TryClaimNextQueuedRunAsync(now, "worker-a", LeaseDuration, CancellationToken.None);
        var secondClaim = await secondQueue.TryClaimNextQueuedRunAsync(now.AddSeconds(1), "worker-b", LeaseDuration, CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.Equal(runId, firstClaim.Run.Id);
        Assert.Null(secondClaim);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_TwoSimultaneousAttemptsProduceExactlyOneClaim()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Can concurrent workers claim the same queued run?");
        using var startGate = new ManualResetEventSlim(false);

        async Task<ClaimedResearchRun?> TryClaimAsync(string workerId)
        {
            await using var context = _fixture.CreateDbContext();
            var queue = new PostgreSqlResearchRunQueue(context);

            return await Task.Run(async () =>
            {
                startGate.Wait();
                return await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, workerId, LeaseDuration, CancellationToken.None);
            });
        }

        var firstAttempt = TryClaimAsync("worker-a");
        var secondAttempt = TryClaimAsync("worker-b");
        startGate.Set();

        var claims = await Task.WhenAll(firstAttempt, secondAttempt);

        var successfulClaims = claims.Where(claim => claim is not null).ToArray();
        Assert.Single(successfulClaims);
        Assert.Equal(runId, successfulClaims[0]!.Run.Id);
        Assert.Single(claims, claim => claim is null);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_TwoDifferentQueuedRunsCanBeClaimedIndependently()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var firstRunId = await SeedQueuedRunAsync("Can the first queued research run be claimed?");
        var secondRunId = await SeedQueuedRunAsync("Can the second queued research run be claimed?");

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstContext);
        var secondQueue = new PostgreSqlResearchRunQueue(secondContext);

        var firstClaim = await firstQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-a", LeaseDuration, CancellationToken.None);
        var secondClaim = await secondQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-b", LeaseDuration, CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.NotNull(secondClaim);
        Assert.Equal([firstRunId, secondRunId], new[] { firstClaim.Run.Id, secondClaim.Run.Id }.Order().ToArray());
        Assert.All(new[] { firstClaim, secondClaim }, claim => Assert.Equal(ResearchRunStatus.Planning, claim.Run.Status));
    }

    [SkippableFact]
    public async Task SaveProgressAsync_PersistsWorkerStateTransitionsAndRenewsLease()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Are background worker state transitions durable?");

        await using (var context = _fixture.CreateDbContext())
        {
            var queue = new PostgreSqlResearchRunQueue(context);
            var run = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-a", LeaseDuration, CancellationToken.None);

            Assert.NotNull(run);
            run.Run.StartSearching(DateTimeOffset.UtcNow);
            var saved = await queue.SaveProgressAsync(run, DateTimeOffset.UtcNow, LeaseDuration, CancellationToken.None);
            Assert.True(saved);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var savedRun = await verificationContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None);

        Assert.Equal(ResearchRunStatus.Searching, savedRun.Status);
        Assert.NotNull(savedRun.StartedAt);
        Assert.Null(savedRun.CompletedAt);
        Assert.Equal("worker-a", savedRun.ProcessingLeaseOwner);
        Assert.NotNull(savedRun.ProcessingLeaseExpiresAt);
    }

    [SkippableFact]
    public async Task MarkFailedAsync_PersistsFailureStateAndClearsLease()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Are background worker failures durable?");

        await using (var context = _fixture.CreateDbContext())
        {
            var queue = new PostgreSqlResearchRunQueue(context);
            var run = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-a", LeaseDuration, CancellationToken.None);

            Assert.NotNull(run);
            var markedFailed = await queue.MarkFailedAsync(
                run,
                "Research processing failed.",
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            Assert.True(markedFailed);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var savedRun = await verificationContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None);

        Assert.Equal(ResearchRunStatus.Failed, savedRun.Status);
        Assert.Equal("Research processing failed.", savedRun.FailureReason);
        Assert.NotNull(savedRun.CompletedAt);
        Assert.Null(savedRun.ProcessingLeaseOwner);
        Assert.Null(savedRun.ProcessingLeaseExpiresAt);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_ExpiredLeaseCanBeReclaimedAtCurrentStage()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedActiveRunAsync(ResearchRunStatus.Searching, "worker-a", DateTimeOffset.UtcNow.AddMinutes(-1), 1);

        await using var context = _fixture.CreateDbContext();
        var queue = new PostgreSqlResearchRunQueue(context);

        var reclaimed = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-b", LeaseDuration, CancellationToken.None);

        Assert.NotNull(reclaimed);
        Assert.Equal(runId, reclaimed.Run.Id);
        Assert.Equal(ResearchRunStatus.Searching, reclaimed.Run.Status);
        Assert.Equal("worker-b", reclaimed.WorkerInstanceId);
        Assert.Equal(2, reclaimed.LeaseVersion);
        Assert.True(reclaimed.WasReclaimed);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_TerminalRunsCannotBeReclaimed()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        await SeedTerminalRunAsync(ResearchRunStatus.Completed);
        await SeedTerminalRunAsync(ResearchRunStatus.Failed);
        await SeedTerminalRunAsync(ResearchRunStatus.Cancelled);

        await using var context = _fixture.CreateDbContext();
        var queue = new PostgreSqlResearchRunQueue(context);

        var claimed = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-b", LeaseDuration, CancellationToken.None);

        Assert.Null(claimed);
    }

    [SkippableFact]
    public async Task RenewLeaseAsync_HeartbeatExtendsLease()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedQueuedRunAsync("Does heartbeat extend the lease?");
        ClaimedResearchRun claimed;

        await using (var context = _fixture.CreateDbContext())
        {
            var queue = new PostgreSqlResearchRunQueue(context);
            claimed = (await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-a", TimeSpan.FromSeconds(30), CancellationToken.None))!;
            var originalExpiry = claimed.LeaseExpiresAt;

            var renewed = await queue.RenewLeaseAsync(claimed, DateTimeOffset.UtcNow.AddSeconds(10), TimeSpan.FromSeconds(30), CancellationToken.None);

            Assert.True(renewed);
            await using var verificationContext = _fixture.CreateDbContext();
            var savedRun = await verificationContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None);
            Assert.True(savedRun.ProcessingLeaseExpiresAt > originalExpiry);
            Assert.NotNull(savedRun.LastHeartbeatAt);
        }
    }

    [SkippableFact]
    public async Task SaveProgressAsync_OldOwnerCannotOverwriteNewerLease()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedActiveRunAsync(ResearchRunStatus.Searching, "worker-a", DateTimeOffset.UtcNow.AddMinutes(-1), 1);

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstContext);
        var secondQueue = new PostgreSqlResearchRunQueue(secondContext);

        var staleClaim = new ClaimedResearchRun(
            await firstContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None),
            "Can stale ownership overwrite progress?",
            "worker-a",
            1,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            false);
        var newerClaim = await secondQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, "worker-b", LeaseDuration, CancellationToken.None);
        Assert.NotNull(newerClaim);

        staleClaim.Run.StartExtraction(DateTimeOffset.UtcNow);
        var staleSaved = await firstQueue.SaveProgressAsync(staleClaim, DateTimeOffset.UtcNow, LeaseDuration, CancellationToken.None);

        Assert.False(staleSaved);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedRun = await verificationContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None);
        Assert.Equal(ResearchRunStatus.Searching, savedRun.Status);
        Assert.Equal("worker-b", savedRun.ProcessingLeaseOwner);
        Assert.Equal(2, savedRun.ProcessingLeaseVersion);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_TwoConcurrentWorkersClaimDifferentRunsSafely()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        await SeedQueuedRunAsync("Can worker A claim independently?");
        await SeedQueuedRunAsync("Can worker B claim independently?");

        var claims = await ClaimConcurrentlyAsync("worker-a", "worker-b");

        Assert.Equal(2, claims.Count(claim => claim is not null));
        Assert.Equal(2, claims.Where(claim => claim is not null).Select(claim => claim!.Run.Id).Distinct().Count());
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_OnlyOneWorkerReclaimsSingleExpiredRun()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearNonTerminalRunsAsync();

        var runId = await SeedActiveRunAsync(ResearchRunStatus.Evaluating, "worker-a", DateTimeOffset.UtcNow.AddMinutes(-1), 1);

        var claims = await ClaimConcurrentlyAsync("worker-b", "worker-c");

        var successfulClaims = claims.Where(claim => claim is not null).ToArray();
        Assert.Single(successfulClaims);
        Assert.Equal(runId, successfulClaims[0]!.Run.Id);
        Assert.Single(claims, claim => claim is null);
    }

    private async Task<ClaimedResearchRun?[]> ClaimConcurrentlyAsync(params string[] workerIds)
    {
        using var startGate = new ManualResetEventSlim(false);

        var attempts = workerIds.Select(workerId => Task.Run(async () =>
        {
            await using var context = _fixture.CreateDbContext();
            var queue = new PostgreSqlResearchRunQueue(context);
            startGate.Wait();
            return await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, workerId, LeaseDuration, CancellationToken.None);
        })).ToArray();

        startGate.Set();
        return await Task.WhenAll(attempts);
    }

    private async Task ClearNonTerminalRunsAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var activeRuns = await context.ResearchRuns
            .Where(run => run.Status != ResearchRunStatus.Completed &&
                          run.Status != ResearchRunStatus.Failed &&
                          run.Status != ResearchRunStatus.Cancelled)
            .ToListAsync(CancellationToken.None);

        foreach (var activeRun in activeRuns)
        {
            activeRun.Cancel(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> SeedQueuedRunAsync(string questionText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    private async Task<Guid> SeedActiveRunAsync(
        ResearchRunStatus status,
        string owner,
        DateTimeOffset leaseExpiresAt,
        long leaseVersion)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Can active research work recover after a crash?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        run.StartPlanning(DateTimeOffset.UtcNow.AddMinutes(-10));
        if (status is ResearchRunStatus.Searching or ResearchRunStatus.Extracting or ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing)
        {
            run.StartSearching(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        if (status is ResearchRunStatus.Extracting or ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing)
        {
            run.StartExtraction(DateTimeOffset.UtcNow.AddMinutes(-8));
        }

        if (status is ResearchRunStatus.Evaluating or ResearchRunStatus.Synthesizing)
        {
            run.StartEvaluation(DateTimeOffset.UtcNow.AddMinutes(-7));
        }

        if (status is ResearchRunStatus.Synthesizing)
        {
            run.StartSynthesis(DateTimeOffset.UtcNow.AddMinutes(-6));
        }

        run.AssignLease(owner, DateTimeOffset.UtcNow.AddMinutes(-10), leaseExpiresAt, leaseVersion);
        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);
        return run.Id;
    }

    private async Task<Guid> SeedTerminalRunAsync(ResearchRunStatus status)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Can terminal research work stay terminal?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        run.StartPlanning(DateTimeOffset.UtcNow.AddMinutes(-10));
        run.StartSearching(DateTimeOffset.UtcNow.AddMinutes(-9));
        run.StartExtraction(DateTimeOffset.UtcNow.AddMinutes(-8));
        run.StartEvaluation(DateTimeOffset.UtcNow.AddMinutes(-7));
        run.StartSynthesis(DateTimeOffset.UtcNow.AddMinutes(-6));

        if (status == ResearchRunStatus.Completed)
        {
            run.Complete(DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        else if (status == ResearchRunStatus.Failed)
        {
            run.Fail("Terminal failure.", DateTimeOffset.UtcNow.AddMinutes(-5));
        }
        else
        {
            run.Cancel(DateTimeOffset.UtcNow.AddMinutes(-5));
        }

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);
        return run.Id;
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}

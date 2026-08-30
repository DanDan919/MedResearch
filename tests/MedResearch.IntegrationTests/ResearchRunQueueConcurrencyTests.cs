using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchRunQueueConcurrencyTests
{
    private readonly PostgreSqlFixture _fixture;

    public ResearchRunQueueConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_ClaimsQueuedRun()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearQueuedRunsAsync();

        var runId = await SeedQueuedRunAsync("Can queued research work be claimed durably?");

        await using var context = _fixture.CreateDbContext();
        var queue = new PostgreSqlResearchRunQueue(context);

        var claimedRun = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(claimedRun);
        Assert.Equal(runId, claimedRun.Run.Id);
        Assert.Equal(ResearchRunStatus.Planning, claimedRun.Run.Status);
        Assert.NotNull(claimedRun.Run.StartedAt);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_ClaimedRunIsNotClaimableAgain()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearQueuedRunsAsync();

        var runId = await SeedQueuedRunAsync("Can a claimed research run be claimed twice?");

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstContext);
        var secondQueue = new PostgreSqlResearchRunQueue(secondContext);

        var firstClaim = await firstQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var secondClaim = await secondQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.Equal(runId, firstClaim.Run.Id);
        Assert.Null(secondClaim);
    }

    [SkippableFact]
    public async Task TryClaimNextQueuedRunAsync_TwoSimultaneousAttemptsProduceExactlyOneClaim()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearQueuedRunsAsync();

        var runId = await SeedQueuedRunAsync("Can concurrent workers claim the same queued run?");
        using var startGate = new ManualResetEventSlim(false);

        async Task<ClaimedResearchRun?> TryClaimAsync()
        {
            await using var context = _fixture.CreateDbContext();
            var queue = new PostgreSqlResearchRunQueue(context);

            return await Task.Run(async () =>
            {
                startGate.Wait();
                return await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            });
        }

        var firstAttempt = TryClaimAsync();
        var secondAttempt = TryClaimAsync();
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
        await ClearQueuedRunsAsync();

        var firstRunId = await SeedQueuedRunAsync("Can the first queued research run be claimed?");
        var secondRunId = await SeedQueuedRunAsync("Can the second queued research run be claimed?");

        await using var firstContext = _fixture.CreateDbContext();
        await using var secondContext = _fixture.CreateDbContext();
        var firstQueue = new PostgreSqlResearchRunQueue(firstContext);
        var secondQueue = new PostgreSqlResearchRunQueue(secondContext);

        var firstClaim = await firstQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        var secondClaim = await secondQueue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.NotNull(secondClaim);
        Assert.Equal([firstRunId, secondRunId], new[] { firstClaim.Run.Id, secondClaim.Run.Id }.Order().ToArray());
        Assert.All(new[] { firstClaim, secondClaim }, claim => Assert.Equal(ResearchRunStatus.Planning, claim.Run.Status));
    }

    [SkippableFact]
    public async Task SaveProgressAsync_PersistsWorkerStateTransitions()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearQueuedRunsAsync();

        var runId = await SeedQueuedRunAsync("Are background worker state transitions durable?");

        await using (var context = _fixture.CreateDbContext())
        {
            var queue = new PostgreSqlResearchRunQueue(context);
            var run = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.NotNull(run);
            run.Run.StartSearching(DateTimeOffset.UtcNow);
            await queue.SaveProgressAsync(run, CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var savedRun = await verificationContext.ResearchRuns.SingleAsync(run => run.Id == runId, CancellationToken.None);

        Assert.Equal(ResearchRunStatus.Searching, savedRun.Status);
        Assert.NotNull(savedRun.StartedAt);
        Assert.Null(savedRun.CompletedAt);
    }

    [SkippableFact]
    public async Task MarkFailedAsync_PersistsFailureState()
    {
        SkipIfPostgreSqlUnavailable();
        await ClearQueuedRunsAsync();

        var runId = await SeedQueuedRunAsync("Are background worker failures durable?");

        await using (var context = _fixture.CreateDbContext())
        {
            var queue = new PostgreSqlResearchRunQueue(context);
            var run = await queue.TryClaimNextQueuedRunAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.NotNull(run);
            var markedFailed = await queue.MarkFailedAsync(
                run.Run.Id,
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
    }

    private async Task ClearQueuedRunsAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var queuedRuns = await context.ResearchRuns
            .Where(run => run.Status == ResearchRunStatus.Queued)
            .ToListAsync(CancellationToken.None);

        foreach (var queuedRun in queuedRuns)
        {
            queuedRun.Cancel(DateTimeOffset.UtcNow);
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

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}

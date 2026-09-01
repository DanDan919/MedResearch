using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class ResearchRunTests
{
    [Fact]
    public void ResearchRun_StartsQueued()
    {
        var run = CreateRun();

        Assert.Equal(ResearchRunStatus.Queued, run.Status);
        Assert.Null(run.StartedAt);
        Assert.Null(run.CompletedAt);
    }

    [Fact]
    public void ResearchRun_AllowsValidLifecycleTransitions()
    {
        var run = CreateRun();
        var startedAt = DateTimeOffset.UtcNow;

        run.StartPlanning(startedAt);
        run.StartSearching(startedAt.AddMinutes(1));
        run.StartExtraction(startedAt.AddMinutes(2));
        run.StartEvaluation(startedAt.AddMinutes(3));
        run.StartSynthesis(startedAt.AddMinutes(4));

        Assert.Equal(ResearchRunStatus.Synthesizing, run.Status);
        Assert.Equal(startedAt, run.StartedAt);
        Assert.Null(run.CompletedAt);
    }

    [Fact]
    public void ResearchRun_RejectsInvalidLifecycleTransition()
    {
        var run = CreateRun();

        Assert.Throws<InvalidOperationException>(() => run.StartSearching(DateTimeOffset.UtcNow));
        Assert.Equal(ResearchRunStatus.Queued, run.Status);
    }

    [Fact]
    public void Complete_RequiresSynthesisStatus()
    {
        var run = CreateRun();

        Assert.Throws<InvalidOperationException>(() => run.Complete(DateTimeOffset.UtcNow));
        Assert.Equal(ResearchRunStatus.Queued, run.Status);
    }

    [Fact]
    public void Complete_MarksRunCompleted()
    {
        var run = CreateRunInSynthesis();
        var completedAt = DateTimeOffset.UtcNow;

        run.Complete(completedAt);

        Assert.Equal(ResearchRunStatus.Completed, run.Status);
        Assert.Equal(completedAt, run.CompletedAt);
    }

    [Fact]
    public void Fail_RequiresFailureReason()
    {
        var run = CreateRun();

        Assert.Throws<ArgumentException>(() => run.Fail(" ", DateTimeOffset.UtcNow));
        Assert.Equal(ResearchRunStatus.Queued, run.Status);
    }

    [Fact]
    public void Fail_MarksRunFailed()
    {
        var run = CreateRun();
        var failedAt = DateTimeOffset.UtcNow;

        run.Fail("PubMed request failed.", failedAt);

        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal("PubMed request failed.", run.FailureReason);
        Assert.Equal(failedAt, run.CompletedAt);
    }

    [Fact]
    public void Cancel_MarksRunCancelled()
    {
        var run = CreateRun();
        var cancelledAt = DateTimeOffset.UtcNow;

        run.Cancel(cancelledAt);

        Assert.Equal(ResearchRunStatus.Cancelled, run.Status);
        Assert.Equal(cancelledAt, run.CompletedAt);
    }

    [Fact]
    public void TerminalRuns_CannotTransitionAgain()
    {
        var run = CreateRunInSynthesis();
        run.Complete(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => run.Cancel(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => run.Fail("Too late.", DateTimeOffset.UtcNow));
        Assert.Equal(ResearchRunStatus.Completed, run.Status);
    }


    [Fact]
    public void AssignLease_RequiresIncreasingVersion()
    {
        var run = CreateRun();
        run.StartPlanning(DateTimeOffset.UtcNow);
        var acquiredAt = DateTimeOffset.UtcNow;

        run.AssignLease("worker-a", acquiredAt, acquiredAt.AddMinutes(5), 1);

        Assert.Equal("worker-a", run.ProcessingLeaseOwner);
        Assert.Equal(1, run.ProcessingLeaseVersion);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            run.AssignLease("worker-b", acquiredAt.AddMinutes(1), acquiredAt.AddMinutes(6), 1));
    }

    [Fact]
    public void Complete_ClearsProcessingLease()
    {
        var run = CreateRunInSynthesis();
        var acquiredAt = DateTimeOffset.UtcNow;
        run.AssignLease("worker-a", acquiredAt, acquiredAt.AddMinutes(5), 1);

        run.Complete(acquiredAt.AddMinutes(1));

        Assert.Null(run.ProcessingLeaseOwner);
        Assert.Null(run.ProcessingLeaseAcquiredAt);
        Assert.Null(run.ProcessingLeaseExpiresAt);
        Assert.Null(run.LastHeartbeatAt);
        Assert.Equal(1, run.ProcessingLeaseVersion);
    }
    private static ResearchRun CreateRun()
    {
        return new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    private static ResearchRun CreateRunInSynthesis()
    {
        var run = CreateRun();
        var startedAt = DateTimeOffset.UtcNow;

        run.StartPlanning(startedAt);
        run.StartSearching(startedAt.AddMinutes(1));
        run.StartExtraction(startedAt.AddMinutes(2));
        run.StartEvaluation(startedAt.AddMinutes(3));
        run.StartSynthesis(startedAt.AddMinutes(4));

        return run;
    }
}

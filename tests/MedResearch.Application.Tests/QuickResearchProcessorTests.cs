using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class QuickResearchProcessorTests
{
    [Fact]
    public async Task EmptyQueue_DoesNotGenerateAResult()
    {
        var store = new RecordingStore { Claim = null };
        Assert.False(await Processor(store).ProcessNextAsync("q0", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Null(store.Result);
        Assert.False(store.Failed);
    }

    [Fact]
    public async Task CancellationAfterClaim_DoesNotMarkScientificFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore { AfterClaim = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(store)
            .ProcessNextAsync("q0", TimeSpan.FromMinutes(1), cancellation.Token));
        Assert.Null(store.Result);
        Assert.False(store.Failed);
    }

    [Fact]
    public async Task CompletionFailure_UsesFailurePortWithoutPersistingExceptionText()
    {
        var store = new RecordingStore { ThrowOnComplete = true };
        Assert.True(await Processor(store).ProcessNextAsync("q0", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.True(store.Failed);
        Assert.NotNull(store.Result);
        Assert.True(store.Result.IsSynthetic);
        Assert.Empty(store.Result.Facts);
        Assert.Empty(store.Result.Sources);
    }

    [Fact]
    public async Task StaleCompletion_IsNotRetriedOrOverwrittenAsFailure()
    {
        var store = new RecordingStore { Saved = false };
        Assert.True(await Processor(store).ProcessNextAsync("q0", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal(1, store.Completions);
        Assert.False(store.Failed);
    }

    private static QuickResearchProcessor Processor(RecordingStore store) => new(store, NullLogger<QuickResearchProcessor>.Instance);

    private sealed class RecordingStore : IQuickResearchStore
    {
        public QuickResearchClaim? Claim { get; init; } = new(Guid.NewGuid(), "q0", 1);
        public Action? AfterClaim { get; init; }
        public bool ThrowOnComplete { get; init; }
        public bool Saved { get; init; } = true;
        public bool Failed { get; private set; }
        public int Completions { get; private set; }
        public QuickResearchResult? Result { get; private set; }

        public Task<QuickResearchClaim?> ClaimAsync(string workerId, TimeSpan leaseDuration, CancellationToken token)
        {
            AfterClaim?.Invoke();
            return Task.FromResult(Claim);
        }

        public Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token)
        {
            Completions++;
            Result = result;
            if (ThrowOnComplete) throw new InvalidOperationException("Untrusted provider or database detail");
            return Task.FromResult(Saved);
        }

        public Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token)
        {
            Failed = true;
            return Task.FromResult(true);
        }

        public Task CreateAsync(QuickResearchRun run, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> SaveRetrievalAsync(QuickResearchClaim claim, QuickRetrievalSnapshot snapshot, bool settle, CancellationToken token) => throw new NotSupportedException();
        public Task<QuickResearchDetails?> FindAsync(Guid runId, string owner, CancellationToken token) => throw new NotSupportedException();
        public Task<QuickResearchHistory> ListAsync(string owner, int page, int pageSize, CancellationToken token) => throw new NotSupportedException();
    }
}

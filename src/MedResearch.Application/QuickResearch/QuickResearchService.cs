using MedResearch.Application.Security;
using MedResearch.Domain.QuickResearch;

namespace MedResearch.Application.QuickResearch;

public sealed record QuickResearchDetails(Guid Id, string Question, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? SafeError, QuickResearchResult? Result);
public sealed record QuickResearchSummary(Guid Id, string Question, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record QuickResearchHistory(QuickResearchSummary[] Items, int Page, int PageSize, int TotalCount);
public sealed record QuickResearchClaim(Guid RunId, string WorkerId, long LeaseVersion);

public interface IQuickResearchStore
{
    Task CreateAsync(QuickResearchRun run, CancellationToken token);
    Task<QuickResearchDetails?> FindAsync(Guid runId, string owner, CancellationToken token);
    Task<QuickResearchHistory> ListAsync(string owner, int page, int pageSize, CancellationToken token);
    Task<QuickResearchClaim?> ClaimAsync(string workerId, TimeSpan leaseDuration, CancellationToken token);
    Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token);
    Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token);
}

public sealed class QuickResearchService(IQuickResearchStore store, ICurrentActor actor)
{
    public async Task<QuickResearchDetails> SubmitAsync(string question, CancellationToken token)
    {
        var run = new QuickResearchRun(question, actor.RequireSubjectId(), DateTimeOffset.UtcNow);
        await store.CreateAsync(run, token);
        return new(run.Id, run.Question, run.Status.ToString(), run.CreatedAt, null, null, null, null);
    }

    public Task<QuickResearchDetails?> FindAsync(Guid id, CancellationToken token) => store.FindAsync(id, actor.RequireSubjectId(), token);

    public Task<QuickResearchHistory> HistoryAsync(int page, int pageSize, CancellationToken token)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
            throw new ArgumentException("Quick history requires page 1..100000 and pageSize 1..50.");
        return store.ListAsync(actor.RequireSubjectId(), page, pageSize, token);
    }
}

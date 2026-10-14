using MedResearch.Application.Security;
using MedResearch.Domain.QuickResearch;
using MedResearch.Application.Research.Admission;

namespace MedResearch.Application.QuickResearch;

public sealed record QuickResearchDetails(Guid Id, string Question, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? SafeError, QuickResearchResult? Result, QuickRetrievalResult? Retrieval = null,
    QuickAnswer? Answer = null, string? AnswerStatus = null);
public sealed record QuickResearchSummary(Guid Id, string Question, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record QuickResearchHistory(QuickResearchSummary[] Items, int Page, int PageSize, int TotalCount);
public sealed record QuickResearchClaim(Guid RunId, string WorkerId, long LeaseVersion, string? Question = null,
    QuickRetrievalSnapshot? Retrieval = null, QuickAnswerGeneration? AnswerGeneration = null);
public sealed record QuickRetrievalAttempt(Guid Id, string Provider, string Query, string? ExecutedQuery, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, string Outcome, int? ReturnedCount, string? FailureCategory);
public sealed record QuickRetrievalResult(Guid QuickRunId, string QueryVersion, string Language, string[] Queries,
    string Outcome, bool IsFinal, QuickPublication[] Sources, QuickRetrievalAttempt[] Attempts, int RejectedCount, string[] Limitations)
{
    public static QuickRetrievalResult From(QuickRetrievalSnapshot snapshot) => new(snapshot.QuickRunId, snapshot.Plan.Version,
        snapshot.Plan.Language, snapshot.Plan.Queries, snapshot.Outcome.ToString(), snapshot.IsFinal, snapshot.Sources,
        snapshot.Attempts.Select(a => new QuickRetrievalAttempt(a.Id, a.Provider, a.Query, a.ExecutedQuery, a.StartedAt,
            a.FinishedAt, a.Outcome.ToString(), a.ReturnedCount, a.FailureCategory)).ToArray(), snapshot.RejectedCount, snapshot.Limitations);
}

public interface IQuickResearchStore
{
    Task<QuickResearchDetails> CreateAsync(QuickResearchRun run, Guid key, CancellationToken token);
    Task<QuickResearchDetails?> FindAsync(Guid runId, string owner, CancellationToken token);
    Task<QuickResearchHistory> ListAsync(string owner, int page, int pageSize, CancellationToken token);
    Task<QuickResearchClaim?> ClaimAsync(string workerId, TimeSpan leaseDuration, CancellationToken token);
    Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token);
    Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token);
    Task<bool> SaveRetrievalAsync(QuickResearchClaim claim, QuickRetrievalSnapshot snapshot, bool settle, CancellationToken token);
}

public sealed class QuickResearchService(IQuickResearchStore store, ICurrentActor actor)
{
    public async Task<QuickResearchDetails> SubmitAsync(string question, Guid key, CancellationToken token)
    {
        if (key == Guid.Empty) throw new ResearchAdmissionException(ResearchAdmissionFailure.InvalidKey);
        var run = new QuickResearchRun(question, actor.RequireSubjectId(), DateTimeOffset.UtcNow);
        return await store.CreateAsync(run, key, token);
    }

    public Task<QuickResearchDetails?> FindAsync(Guid id, CancellationToken token) => store.FindAsync(id, actor.RequireSubjectId(), token);

    public Task<QuickResearchHistory> HistoryAsync(int page, int pageSize, CancellationToken token)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
            throw new ArgumentException("Quick history requires page 1..100000 and pageSize 1..50.");
        return store.ListAsync(actor.RequireSubjectId(), page, pageSize, token);
    }
}

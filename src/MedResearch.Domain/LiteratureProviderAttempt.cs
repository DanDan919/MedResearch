namespace MedResearch.Domain;

public enum LiteratureProviderAttemptStatus { Started, SucceededWithResults, SucceededZeroResults, Failed, TimedOut, Cancelled }
public enum LiteratureProviderFailureCategory { NetworkFailure, Timeout, RateLimited, InvalidResponse, ProviderProtocolError, ResponseTooLarge, Cancelled, UnexpectedFailure }

public sealed class LiteratureProviderAttempt
{
    private LiteratureProviderAttempt() { }

    public LiteratureProviderAttempt(Guid id, Guid researchRunId, Guid researchPlanId, string source, string query, DateTimeOffset startedAt)
    {
        if (id == Guid.Empty || researchRunId == Guid.Empty || researchPlanId == Guid.Empty)
            throw new ArgumentException("Attempt, run and plan IDs are required.");
        if (string.IsNullOrWhiteSpace(source) || source.Length > 64 || string.IsNullOrWhiteSpace(query) || query.Length > 2000)
            throw new ArgumentException("A bounded provider and query are required.");
        Id = id;
        ResearchRunId = researchRunId;
        ResearchPlanId = researchPlanId;
        Source = source.Trim();
        Query = query.Trim();
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid ResearchRunId { get; private set; }
    public Guid ResearchPlanId { get; private set; }
    public string Source { get; private set; } = null!;
    public string Query { get; private set; } = null!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public LiteratureProviderAttemptStatus Status { get; private set; } = LiteratureProviderAttemptStatus.Started;
    public int? ResultCount { get; private set; }
    public LiteratureProviderFailureCategory? FailureCategory { get; private set; }
    public Guid? LiteratureSearchId { get; private set; }

    public void Succeed(Guid searchId, int resultCount, DateTimeOffset completedAt)
    {
        if (searchId == Guid.Empty || resultCount < 0) throw new ArgumentException("A valid search and count are required.");
        Finish(resultCount == 0 ? LiteratureProviderAttemptStatus.SucceededZeroResults : LiteratureProviderAttemptStatus.SucceededWithResults, completedAt);
        LiteratureSearchId = searchId;
        ResultCount = resultCount;
    }

    public void Fail(LiteratureProviderFailureCategory category, DateTimeOffset completedAt)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        Finish(category switch
        {
            LiteratureProviderFailureCategory.Timeout => LiteratureProviderAttemptStatus.TimedOut,
            LiteratureProviderFailureCategory.Cancelled => LiteratureProviderAttemptStatus.Cancelled,
            _ => LiteratureProviderAttemptStatus.Failed
        }, completedAt);
        FailureCategory = category;
    }

    private void Finish(LiteratureProviderAttemptStatus status, DateTimeOffset completedAt)
    {
        if (Status != LiteratureProviderAttemptStatus.Started) throw new InvalidOperationException("A finished provider attempt is immutable.");
        if (completedAt < StartedAt) throw new ArgumentException("Completion precedes start.");
        Status = status;
        CompletedAt = completedAt;
    }
}

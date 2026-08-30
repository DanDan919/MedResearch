namespace MedResearch.Domain;

public sealed class ResearchRun
{
    public ResearchRun(Guid researchQuestionId, DateTimeOffset createdAt)
        : this(Guid.NewGuid(), researchQuestionId, ResearchRunStatus.Queued, createdAt, null, null, null)
    {
    }

    public ResearchRun(
        Guid id,
        Guid researchQuestionId,
        ResearchRunStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? failureReason)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(id));
        }

        if (researchQuestionId == Guid.Empty)
        {
            throw new ArgumentException("Research question id cannot be empty.", nameof(researchQuestionId));
        }

        Id = id;
        ResearchQuestionId = researchQuestionId;
        Status = status;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
    }

    public Guid Id { get; }

    public Guid ResearchQuestionId { get; }

    public ResearchRunStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public void StartPlanning(DateTimeOffset startedAt) => MoveTo(ResearchRunStatus.Planning, ResearchRunStatus.Queued, startedAt);

    public void StartSearching(DateTimeOffset startedAt) => MoveTo(ResearchRunStatus.Searching, ResearchRunStatus.Planning, startedAt);

    public void StartExtraction(DateTimeOffset startedAt) => MoveTo(ResearchRunStatus.Extracting, ResearchRunStatus.Searching, startedAt);

    public void StartEvaluation(DateTimeOffset startedAt) => MoveTo(ResearchRunStatus.Evaluating, ResearchRunStatus.Extracting, startedAt);

    public void StartSynthesis(DateTimeOffset startedAt) => MoveTo(ResearchRunStatus.Synthesizing, ResearchRunStatus.Evaluating, startedAt);

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureCurrentStatus(ResearchRunStatus.Synthesizing, ResearchRunStatus.Completed);
        Status = ResearchRunStatus.Completed;
        CompletedAt = completedAt;
    }

    public void Fail(string failureReason, DateTimeOffset failedAt)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));
        }

        EnsureNotTerminal(ResearchRunStatus.Failed);
        Status = ResearchRunStatus.Failed;
        FailureReason = failureReason.Trim();
        CompletedAt = failedAt;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        EnsureNotTerminal(ResearchRunStatus.Cancelled);
        Status = ResearchRunStatus.Cancelled;
        CompletedAt = cancelledAt;
    }

    private void MoveTo(ResearchRunStatus nextStatus, ResearchRunStatus requiredCurrentStatus, DateTimeOffset startedAt)
    {
        EnsureCurrentStatus(requiredCurrentStatus, nextStatus);

        Status = nextStatus;
        StartedAt ??= startedAt;
    }

    private void EnsureCurrentStatus(ResearchRunStatus requiredCurrentStatus, ResearchRunStatus nextStatus)
    {
        if (Status != requiredCurrentStatus)
        {
            throw new InvalidOperationException(
                $"Cannot move a research run from {Status} to {nextStatus}. Expected current status is {requiredCurrentStatus}.");
        }
    }

    private void EnsureNotTerminal(ResearchRunStatus nextStatus)
    {
        if (Status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot move a terminal research run from {Status} to {nextStatus}.");
        }
    }
}

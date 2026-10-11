using MedResearch.Application.Security;

namespace MedResearch.Application.Research.Synthesis;

public sealed record SynthesisRetryState(
    Guid ResearchRunId,
    string Availability,
    Guid? RetryAttemptId,
    string? AttemptStatus,
    int GenerationBudget,
    int GenerationCallsReserved,
    string? FailureCategory);

public sealed class SynthesisRetryConflictException : Exception
{
    public SynthesisRetryConflictException() : base("Report retry is not available for this run.") { }
}

public sealed class SynthesisRetryBudgetException : Exception
{
    public SynthesisRetryBudgetException(bool outcomeUnknown)
        : base(outcomeUnknown ? "A report generation outcome is unknown. Operator review is required." : "The report generation attempt budget is exhausted.")
    {
        OutcomeUnknown = outcomeUnknown;
    }

    public bool OutcomeUnknown { get; }
}

public interface ISynthesisRetryStore
{
    Task<SynthesisRetryState?> GetAsync(Guid runId, string owner, CancellationToken cancellationToken);
    Task<SynthesisRetryState?> AdmitAsync(Guid runId, string owner, Guid idempotencyKey, CancellationToken cancellationToken);
}

public interface ISynthesisRetryGenerationBudget
{
    Task ValidateInputsAsync(Guid runId, CancellationToken cancellationToken);
    Task ReserveAsync(Guid runId, CancellationToken cancellationToken);
    Task RecordResponseAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed class SynthesisRetryUseCase(ISynthesisRetryStore store, ICurrentActor actor)
{
    public Task<SynthesisRetryState?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        store.GetAsync(runId, actor.RequireSubjectId(), cancellationToken);

    public Task<SynthesisRetryState?> RequestAsync(Guid runId, string? key, CancellationToken cancellationToken)
    {
        var owner = actor.RequireSubjectId();
        if (!Guid.TryParse(key, out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException("A valid retry idempotency key is required.");
        return store.AdmitAsync(runId, owner, parsed, cancellationToken);
    }
}

namespace MedResearch.Infrastructure.Persistence;

// One owner-authorized retry per run. A pending generation is potentially billed.
public sealed class SynthesisRetryAttemptEntity
{
    public Guid Id { get; set; }
    public Guid ResearchRunId { get; set; }
    public string RequestedByOwner { get; set; } = null!;
    public string IdempotencyKeyHash { get; set; } = null!;
    public string InputFingerprint { get; set; } = null!;
    public string Status { get; set; } = "Queued";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? LeaseVersion { get; set; }
    public int GenerationBudget { get; set; }
    public int GenerationCallsReserved { get; set; }
    public bool GenerationPending { get; set; }
    public string? FailureCategory { get; set; }
}

namespace MedResearch.Application.Research.Admission;

public enum ResearchAdmissionFailure
{
    InvalidKey,
    IdempotencyConflict,
    OwnerOutstanding,
    GlobalOutstanding,
    OwnerDaily,
    GlobalDaily,
    Stopped
}

public sealed class ResearchAdmissionException(ResearchAdmissionFailure failure, int? retryAfterSeconds = null)
    : Exception("Research admission rejected: " + failure)
{
    public ResearchAdmissionFailure Failure { get; } = failure;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

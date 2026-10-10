namespace MedResearch.Infrastructure.Persistence;

public sealed class ResearchAdmissionEntity
{
    public string OwnerSubjectId { get; set; } = "";
    public Guid IdempotencyKey { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public Guid ResearchRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

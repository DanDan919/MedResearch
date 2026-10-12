namespace MedResearch.Domain.QuickResearch;

public enum QuickResearchStatus { Queued, Processing, Completed, Failed }

public sealed class QuickResearchRun
{
    private QuickResearchRun() { }

    public QuickResearchRun(string question, string ownerSubjectId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Trim().Length > 2000)
            throw new ArgumentException("Quick question must contain 1..2000 characters.", nameof(question));
        Id = Guid.NewGuid();
        Question = question.Trim();
        OwnerSubjectId = ResearchOwnership.NormalizeSubject(ownerSubjectId);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string OwnerSubjectId { get; private set; } = "";
    public string Question { get; private set; } = "";
    public QuickResearchStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? SafeError { get; private set; }
    public QuickResearchResult? Result { get; private set; }
    public QuickRetrievalSnapshot? Retrieval { get; private set; }
    public QuickAnswerGeneration? AnswerGeneration { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public long LeaseVersion { get; private set; }

    public void StartProcessing(DateTimeOffset now)
    {
        Require(QuickResearchStatus.Queued);
        Status = QuickResearchStatus.Processing;
        StartedAt = now;
    }

    public void Complete(QuickResearchResult result, DateTimeOffset now)
    {
        Require(QuickResearchStatus.Processing);
        result.ValidateQ0(Id);
        Result = result;
        Status = QuickResearchStatus.Completed;
        CompletedAt = now;
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    private void Require(QuickResearchStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException("Invalid Quick research lifecycle transition.");
    }
}

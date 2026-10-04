namespace MedResearch.Domain;


public sealed class ResearchQuestion
{
    public ResearchQuestion(string text, DateTimeOffset createdAt)
        : this(Guid.NewGuid(), text, createdAt, ResearchOwnership.LegacyUnownedSubjectId)
    {
    }

    public ResearchQuestion(string text, DateTimeOffset createdAt, string ownerSubjectId)
        : this(Guid.NewGuid(), text, createdAt, ownerSubjectId)
    {
    }

    public ResearchQuestion(Guid id, string text, DateTimeOffset createdAt)
        : this(id, text, createdAt, ResearchOwnership.LegacyUnownedSubjectId)
    {
    }

    public ResearchQuestion(Guid id, string text, DateTimeOffset createdAt, string ownerSubjectId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research question id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Research question text is required.", nameof(text));
        }

        ownerSubjectId = ResearchOwnership.NormalizeSubject(ownerSubjectId);

        Id = id;
        Text = text.Trim();
        CreatedAt = createdAt;
        OwnerSubjectId = ownerSubjectId;
    }

    public Guid Id { get; }

    public string Text { get; }

    public DateTimeOffset CreatedAt { get; }

    public string OwnerSubjectId { get; }
}

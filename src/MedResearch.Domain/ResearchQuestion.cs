namespace MedResearch.Domain;

public sealed class ResearchQuestion
{
    public ResearchQuestion(string text, DateTimeOffset createdAt)
        : this(Guid.NewGuid(), text, createdAt)
    {
    }

    public ResearchQuestion(Guid id, string text, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research question id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Research question text is required.", nameof(text));
        }

        Id = id;
        Text = text.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Text { get; }

    public DateTimeOffset CreatedAt { get; }
}

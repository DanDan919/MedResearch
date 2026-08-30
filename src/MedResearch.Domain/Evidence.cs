namespace MedResearch.Domain;

public sealed class Evidence
{
    public Evidence(Guid id, Guid studyId, string claim, EvidenceDirection direction, decimal? confidence)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Evidence id cannot be empty.", nameof(id));
        }

        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        if (string.IsNullOrWhiteSpace(claim))
        {
            throw new ArgumentException("Evidence claim is required.", nameof(claim));
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1 when present.");
        }

        Id = id;
        StudyId = studyId;
        Claim = claim.Trim();
        Direction = direction;
        Confidence = confidence;
    }

    public Guid Id { get; }

    public Guid StudyId { get; }

    public string Claim { get; }

    public EvidenceDirection Direction { get; }

    public decimal? Confidence { get; }
}

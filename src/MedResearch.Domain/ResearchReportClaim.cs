namespace MedResearch.Domain;

public sealed class ResearchReportClaim
{
    public ResearchReportClaim(
        Guid id,
        Guid researchReportId,
        ResearchReportClaimType claimType,
        ResearchReportClaimDirection direction,
        string text,
        int ordinal)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research report claim id cannot be empty.", nameof(id));
        }

        if (researchReportId == Guid.Empty)
        {
            throw new ArgumentException("Research report id cannot be empty.", nameof(researchReportId));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Research report claim text is required.", nameof(text));
        }

        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Claim ordinal cannot be negative.");
        }

        Id = id;
        ResearchReportId = researchReportId;
        ClaimType = claimType;
        Direction = direction;
        Text = string.Join(' ', text.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
        Ordinal = ordinal;
    }

    public Guid Id { get; }

    public Guid ResearchReportId { get; }

    public ResearchReportClaimType ClaimType { get; }

    public ResearchReportClaimDirection Direction { get; }

    public string Text { get; }

    public int Ordinal { get; }
}
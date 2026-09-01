namespace MedResearch.Domain;

public sealed class ResearchReportClaimEvidence
{
    public ResearchReportClaimEvidence(Guid researchReportClaimId, Guid evidenceId, int ordinal)
    {
        if (researchReportClaimId == Guid.Empty)
        {
            throw new ArgumentException("Research report claim id cannot be empty.", nameof(researchReportClaimId));
        }

        if (evidenceId == Guid.Empty)
        {
            throw new ArgumentException("Evidence id cannot be empty.", nameof(evidenceId));
        }

        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Citation ordinal cannot be negative.");
        }

        ResearchReportClaimId = researchReportClaimId;
        EvidenceId = evidenceId;
        Ordinal = ordinal;
    }

    public Guid ResearchReportClaimId { get; }

    public Guid EvidenceId { get; }

    public int Ordinal { get; }
}
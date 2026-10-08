namespace MedResearch.Domain;

public sealed class ResearchReportClaim
{
    public ResearchReportClaim(
        Guid id,
        Guid researchReportId,
        ResearchReportClaimType claimType,
        ResearchReportClaimDirection direction,
        string text,
        int ordinal,
        ResearchClaimSemantics? semantics = null,
        string? semanticKey = null)
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
        if (!Enum.IsDefined(claimType) || !Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(claimType));
        if (semantics is not null && (semantics.ProtocolVersion != "structured-claim-v1" || !Enum.IsDefined(semantics.Kind) ||
            semantics.Direction != direction || (semantics.Statistic is not null && !Enum.IsDefined(semantics.Statistic.Value)) ||
            semanticKey?.Length != 64)) throw new ArgumentException("Structured claim semantics require a valid protocol, categories and semantic key.", nameof(semantics));
        if (semantics is null && semanticKey is not null)
            throw new ArgumentException("Legacy claims cannot have a structured semantic key.", nameof(semanticKey));
        if (semantics is not null)
        {
            var numericKind = semantics.Kind is ResearchClaimKind.ReportedStudyResult or ResearchClaimKind.QuantitativeSynthesis;
            if (semantics.EvidenceIds.Any(value => value == Guid.Empty) || semantics.EvidenceIds.Distinct().Count() != semantics.EvidenceIds.Count ||
                (numericKind ? semantics.Numeric is null || semantics.Statistic is null : semantics.Numeric is not null || semantics.Statistic is not null) ||
                (semantics.Kind == ResearchClaimKind.ReportedStudyResult && (semantics.EvidenceIds.Count != 1 || semantics.NumericEvidenceId != semantics.EvidenceIds.Single())) ||
                (semantics.Kind != ResearchClaimKind.ReportedStudyResult && semantics.NumericEvidenceId is not null) ||
                (semantics.Kind == ResearchClaimKind.QuantitativeSynthesis ? semantics.QuantitativeArtifactId is null || semantics.GroupKey is null || semantics.SnapshotFingerprint is null : semantics.QuantitativeArtifactId is not null || semantics.GroupKey is not null || semantics.SnapshotFingerprint is not null) ||
                (semantics.Kind == ResearchClaimKind.InsufficientEvidence ? semantics.EvidenceIds.Count != 0 || direction != ResearchReportClaimDirection.NotApplicable : semantics.EvidenceIds.Count == 0) ||
                (semantics.Kind == ResearchClaimKind.MixedEvidence && direction != ResearchReportClaimDirection.Mixed) ||
                (numericKind && direction != ResearchReportClaimDirection.NotApplicable))
                throw new ArgumentException("Structured claim references and kind must be coherent.", nameof(semantics));
        }

        Id = id;
        ResearchReportId = researchReportId;
        ClaimType = claimType;
        Direction = direction;
        Text = string.Join(' ', text.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
        Ordinal = ordinal;
        Semantics = semantics;
        GroundingStatus = semantics is null ? ResearchClaimGroundingStatus.LegacyUnverified : ResearchClaimGroundingStatus.StructuredValidated;
        SemanticKey = semanticKey;
        QuantitativeArtifactId = semantics?.QuantitativeArtifactId;
        NumericEvidenceId = semantics?.NumericEvidenceId;
    }

    public Guid Id { get; }

    public Guid ResearchReportId { get; }

    public ResearchReportClaimType ClaimType { get; }

    public ResearchReportClaimDirection Direction { get; }

    public string Text { get; }

    public int Ordinal { get; }

    public ResearchClaimGroundingStatus GroundingStatus { get; }
    public ResearchClaimSemantics? Semantics { get; }
    public string? SemanticKey { get; }
    public Guid? QuantitativeArtifactId { get; }
    public Guid? NumericEvidenceId { get; }
}

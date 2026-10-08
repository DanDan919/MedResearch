using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

internal static class StructuredClaimReadGuard
{
    public static void AssertValid(ResearchReportClaim claim, IReadOnlyCollection<Guid> evidenceIds,
        IReadOnlyDictionary<Guid, QuantitativeSynthesisArtifactEntity> artifacts)
    {
        if (claim.GroundingStatus == ResearchClaimGroundingStatus.LegacyUnverified)
        {
            if (claim.Semantics is not null || claim.SemanticKey is not null || claim.QuantitativeArtifactId is not null || claim.NumericEvidenceId is not null)
                throw new InvalidOperationException("Legacy claim cannot acquire structured authority implicitly.");
            return;
        }

        var semantics = claim.Semantics;
        if (claim.GroundingStatus != ResearchClaimGroundingStatus.StructuredValidated || semantics is null ||
            semantics.ProtocolVersion != StructuredResearchClaimValidator.ProtocolVersion || !Enum.IsDefined(semantics.Kind) ||
            !Enum.IsDefined(semantics.Direction) || (semantics.Statistic is not null && !Enum.IsDefined(semantics.Statistic.Value)) ||
            claim.Direction != semantics.Direction || claim.NumericEvidenceId != semantics.NumericEvidenceId ||
            claim.QuantitativeArtifactId != semantics.QuantitativeArtifactId ||
            !semantics.EvidenceIds.Order().SequenceEqual(evidenceIds.Distinct().Order()) ||
            claim.SemanticKey != StructuredResearchClaimRenderer.SemanticKey(semantics) ||
            claim.Text != StructuredResearchClaimRenderer.Render(semantics))
            throw new InvalidOperationException("Stored structured claim has incoherent authority or citation lineage.");

        if (semantics.QuantitativeArtifactId is { } artifactId &&
            (!artifacts.TryGetValue(artifactId, out var artifact) || artifact.GroupKey != semantics.GroupKey ||
             artifact.SnapshotFingerprint != semantics.SnapshotFingerprint))
            throw new InvalidOperationException("Stored claim references a foreign or incoherent quantitative artifact.");
    }
}

using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class ResearchClaimSemanticsTests
{
    [Fact]
    public void HistoricalClaimHasNoImplicitStructuredAuthority()
    {
        var claim = new ResearchReportClaim(Guid.NewGuid(), Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Historical text", 0);
        Assert.Equal(ResearchClaimGroundingStatus.LegacyUnverified, claim.GroundingStatus);
        Assert.Null(claim.Semantics);
        Assert.Throws<ArgumentException>(() => new ResearchReportClaim(Guid.NewGuid(), Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Historical text", 0, semanticKey: new string('a', 64)));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("reference")]
    [InlineData("direction")]
    [InlineData("numeric")]
    public void StructuredClaimRejectsIncoherentAuthorityShape(string attack)
    {
        var semantics = new ResearchClaimSemantics("structured-claim-v1", ResearchClaimKind.QualitativeEffect, "recall", "adults", "sleep", null, null,
            ResearchReportClaimDirection.Positive, [Guid.NewGuid()], null, null, null, null, null, null);
        semantics = attack switch
        {
            "kind" => semantics with { Kind = (ResearchClaimKind)999 },
            "reference" => semantics with { NumericEvidenceId = Guid.NewGuid() },
            "direction" => semantics with { Direction = ResearchReportClaimDirection.Negative },
            _ => semantics with { Kind = ResearchClaimKind.QuantitativeSynthesis }
        };
        Assert.Throws<ArgumentException>(() => new ResearchReportClaim(Guid.NewGuid(), Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Backend text", 0, semantics, new string('a', 64)));
    }
}

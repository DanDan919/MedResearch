using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class EvidenceTrustBoundaryTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    public void Constructor_RejectsUndefinedDirection(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((EvidenceDirection)value));
    }

    [Fact]
    public void Constructor_VerifiedRequiresAnchor()
    {
        Assert.Throws<ArgumentException>(() => Create(EvidenceDirection.Positive,
            [new(NumericGroundingField.EffectEstimate, NumericGroundingStatus.Verified, null, null)]));
    }

    private static Evidence Create(EvidenceDirection direction, IReadOnlyCollection<NumericGroundingFact>? facts = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mortality", "Reported result", "Source text", direction,
            EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, null, null, null, null, null, null, null, null, null, null, numericGrounding: facts);
}

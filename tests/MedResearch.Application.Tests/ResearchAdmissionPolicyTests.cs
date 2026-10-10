using MedResearch.Application.Research.Admission;

namespace MedResearch.Application.Tests;

public sealed class ResearchAdmissionPolicyTests
{
    [Fact]
    public void Defaults_AreBoundedPilotPolicy()
    {
        var options = new ResearchAdmissionOptions();
        options.Validate();
        Assert.Equal(1, options.OwnerOutstandingLimit);
        Assert.Equal(2, options.GlobalOutstandingLimit);
        Assert.Equal(2, options.OwnerDailyLimit);
        Assert.Equal(10, options.GlobalDailyLimit);
        Assert.False(options.StopNewAdmissions);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10001)]
    public void EveryLimit_RejectsInvalidBounds(int value)
    {
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { OwnerOutstandingLimit = value }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { GlobalOutstandingLimit = value }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { OwnerDailyLimit = value }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { GlobalDailyLimit = value }.Validate());
    }

    [Fact]
    public void OwnerLimitCannotExceedGlobalLimit()
    {
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { OwnerOutstandingLimit = 3 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ResearchAdmissionOptions { OwnerDailyLimit = 11 }.Validate());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("bad")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void KeyIsRequiredCanonicalNonemptyUuid(string? key)
    {
        var error = Assert.Throws<ResearchAdmissionException>(() => ResearchCreateIdentity.ParseKey(key));
        Assert.Equal(ResearchAdmissionFailure.InvalidKey, error.Failure);
        if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_IsVersionedDeterministicAndContentSensitive()
    {
        Assert.Equal(64, ResearchCreateIdentity.Fingerprint("Accepted trimmed question").Length);
        Assert.Equal(ResearchCreateIdentity.Fingerprint("Accepted trimmed question"), ResearchCreateIdentity.Fingerprint("Accepted trimmed question"));
        Assert.NotEqual(ResearchCreateIdentity.Fingerprint("Accepted trimmed question"), ResearchCreateIdentity.Fingerprint("Different question"));
    }
}

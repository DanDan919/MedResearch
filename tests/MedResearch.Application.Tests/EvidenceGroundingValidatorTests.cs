using MedResearch.Application.Research.Extraction;

namespace MedResearch.Application.Tests;

public sealed class EvidenceGroundingValidatorTests
{
    private readonly EvidenceGroundingValidator _validator = new();

    [Fact]
    public void IsGrounded_AcceptsExactExcerpt()
    {
        Assert.True(_validator.IsGrounded("Sleep improved recall in adults.", "Sleep improved recall"));
    }

    [Fact]
    public void IsGrounded_AcceptsWhitespaceDifferencesAndLineBreaks()
    {
        Assert.True(_validator.IsGrounded("Sleep improved\nrecall   in adults.", "improved recall in adults"));
    }

    [Fact]
    public void IsGrounded_RejectsFabricatedWords()
    {
        Assert.False(_validator.IsGrounded("Sleep improved recall in adults.", "Sleep cured recall in adults."));
    }

    [Fact]
    public void IsGrounded_RejectsBlankSupportingText()
    {
        Assert.False(_validator.IsGrounded("Sleep improved recall in adults.", "   "));
    }

    [Fact]
    public void IsGrounded_RejectsExcessiveExcerptLength()
    {
        var excerpt = new string('a', 1_001);

        Assert.False(_validator.IsGrounded(excerpt, excerpt));
    }
}

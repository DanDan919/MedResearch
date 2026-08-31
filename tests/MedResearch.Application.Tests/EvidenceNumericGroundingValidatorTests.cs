using MedResearch.Application.Research.Extraction;

namespace MedResearch.Application.Tests;

public sealed class EvidenceNumericGroundingValidatorTests
{
    private readonly EvidenceNumericGroundingValidator _validator = new();

    [Theory]
    [InlineData("The trial enrolled 120 adults.", "120")]
    [InlineData("The effect was 1.42 units.", "1.42")]
    [InlineData("The reported p value was 0.03.", "0.03")]
    [InlineData("Response occurred in 95% of participants.", "95")]
    public void IsGrounded_AcceptsNumericValuesPresentInSource(string sourceText, string value)
    {
        Assert.True(_validator.IsGrounded(sourceText, decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void IsGrounded_RejectsAbsentNumericValues()
    {
        Assert.False(_validator.IsGrounded("The study reported improvement without a sample size.", 120));
    }
}

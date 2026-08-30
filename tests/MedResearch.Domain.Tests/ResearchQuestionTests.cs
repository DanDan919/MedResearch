using MedResearch.Domain;

namespace MedResearch.Domain.Tests;

public sealed class ResearchQuestionTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_RejectsEmptyQuestionText(string text)
    {
        Assert.Throws<ArgumentException>(() => new ResearchQuestion(text, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_TrimsValidQuestionText()
    {
        var question = new ResearchQuestion("  Does sleep deprivation alter memory consolidation?  ", DateTimeOffset.UtcNow);

        Assert.Equal("Does sleep deprivation alter memory consolidation?", question.Text);
    }
}

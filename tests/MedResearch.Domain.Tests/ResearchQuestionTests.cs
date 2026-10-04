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

    [Fact]
    public void Constructor_PreservesOpaqueOwnerSubjectWithoutCaseFolding()
    {
        var question = new ResearchQuestion("Does sleep improve memory?", DateTimeOffset.UtcNow, "  UserA  ");

        Assert.Equal("UserA", question.OwnerSubjectId);
    }

    [Fact]
    public void Constructor_RejectsOversizedOwnerSubject()
    {
        Assert.Throws<ArgumentException>(() =>
            new ResearchQuestion("Does sleep improve memory?", DateTimeOffset.UtcNow, new string('x', 201)));
    }
}

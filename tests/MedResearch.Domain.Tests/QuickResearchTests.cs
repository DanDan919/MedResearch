using MedResearch.Domain.QuickResearch;

namespace MedResearch.Domain.Tests;

public sealed class QuickResearchTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Question_MustBePresent(string? question) =>
        Assert.Throws<ArgumentException>(() => new QuickResearchRun(question!, "owner", DateTimeOffset.UtcNow));

    [Fact]
    public void QuestionAndOwner_AreBounded()
    {
        Assert.Throws<ArgumentException>(() => new QuickResearchRun(new string('x', 2001), "owner", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new QuickResearchRun("Question", new string('x', 201), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void QuickLifecycle_HasOneProcessingStep_NotDeepStages()
    {
        var run = new QuickResearchRun(" Question ", "owner", DateTimeOffset.UtcNow);
        Assert.Equal("Question", run.Question);
        Assert.Throws<InvalidOperationException>(() => run.Complete(QuickResearchResult.CreateSynthetic(run.Id), DateTimeOffset.UtcNow));
        run.StartProcessing(DateTimeOffset.UtcNow);
        run.Complete(QuickResearchResult.CreateSynthetic(run.Id), DateTimeOffset.UtcNow);
        Assert.Equal(QuickResearchStatus.Completed, run.Status);
        Assert.True(run.Result!.IsSynthetic);
        Assert.Empty(run.Result.Facts);
        Assert.Throws<InvalidOperationException>(() => run.StartProcessing(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => run.Complete(run.Result, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("foreign-result")]
    [InlineData("foreign-source")]
    [InlineData("unknown-citation")]
    [InlineData("duplicate-source")]
    [InlineData("live-result")]
    [InlineData("live-source")]
    [InlineData("unsafe-url")]
    public void Result_RejectsForeignLineageAndNonSyntheticData(string defect)
    {
        var id = Guid.NewGuid();
        var source = new QuickResearchSource(Guid.NewGuid(), id, "Synthetic fixture", "https://example.invalid/q0", true);
        var result = QuickResearchResult.CreateSynthetic(id) with { Sources = [source], Facts = [new("Synthetic fact", [source.Id])] };
        result = defect switch
        {
            "foreign-result" => result with { QuickRunId = Guid.NewGuid() },
            "foreign-source" => result with { Sources = [source with { QuickRunId = Guid.NewGuid() }] },
            "unknown-citation" => result with { Facts = [new("Synthetic fact", [Guid.NewGuid()])] },
            "duplicate-source" => result with { Sources = [source, source] },
            "live-result" => result with { IsSynthetic = false },
            "live-source" => result with { Sources = [source with { IsSynthetic = false }] },
            _ => result with { Sources = [source with { Url = "javascript:alert(1)" }] }
        };
        Assert.Throws<ArgumentException>(() => result.ValidateQ0(id));
    }
}

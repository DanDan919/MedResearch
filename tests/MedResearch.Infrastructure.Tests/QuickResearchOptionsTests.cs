using MedResearch.Infrastructure.QuickResearch;

namespace MedResearch.Infrastructure.Tests;

public sealed class QuickResearchOptionsTests
{
    [Fact]
    public void DefaultWorker_IsDisabledAndBounded()
    {
        var options = new QuickResearchOptions();
        options.Validate();
        Assert.False(options.Enabled);
        Assert.Equal(2, options.PollIntervalSeconds);
        Assert.Equal(60, options.LeaseDurationSeconds);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(61, 60)]
    [InlineData(2, 4)]
    [InlineData(2, 301)]
    public void InvalidWorkerBounds_AreRejected(int poll, int lease) => Assert.Throws<InvalidOperationException>(() =>
        new QuickResearchOptions { PollIntervalSeconds = poll, LeaseDurationSeconds = lease }.Validate());
}

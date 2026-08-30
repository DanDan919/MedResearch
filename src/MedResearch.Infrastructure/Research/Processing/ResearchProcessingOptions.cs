namespace MedResearch.Infrastructure.Research.Processing;

public sealed class ResearchProcessingOptions
{
    public const string SectionName = "ResearchProcessing";

    public bool Enabled { get; init; } = true;

    public int IdleDelayMilliseconds { get; init; } = 1_000;

    public TimeSpan IdleDelay => TimeSpan.FromMilliseconds(Math.Max(100, IdleDelayMilliseconds));
}

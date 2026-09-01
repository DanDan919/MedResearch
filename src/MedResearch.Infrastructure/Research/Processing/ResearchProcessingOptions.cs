namespace MedResearch.Infrastructure.Research.Processing;

public sealed class ResearchProcessingOptions
{
    public const string SectionName = "ResearchProcessing";

    public bool Enabled { get; init; } = true;

    public int IdleDelayMilliseconds { get; init; } = 1_000;

    public int LeaseDurationSeconds { get; init; } = 900;

    public int HeartbeatIntervalSeconds { get; init; } = 60;

    public TimeSpan IdleDelay => TimeSpan.FromMilliseconds(Math.Max(100, IdleDelayMilliseconds));

    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
}
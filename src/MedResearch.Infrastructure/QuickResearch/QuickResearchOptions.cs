namespace MedResearch.Infrastructure.QuickResearch;

public sealed class QuickResearchOptions
{
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 2;
    public int LeaseDurationSeconds { get; set; } = 60;

    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 60 || LeaseDurationSeconds is < 5 or > 300)
            throw new InvalidOperationException("QuickResearch poll interval must be 1..60s and lease duration 5..300s.");
    }
}

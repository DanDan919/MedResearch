namespace MedResearch.Infrastructure.SourceMaterials.EuropePmc;

public sealed class EuropePmcFullTextOptions
{
    public const string SectionName = "EuropePmcFullText";
    public const int MaximumContentCharactersLimit = 200_000;

    public bool Enabled { get; init; } = true;

    public int MaxContentCharacters { get; init; } = 30_000;

    public int TimeoutSeconds { get; init; } = 15;

    public int MaxRetryAttempts { get; init; } = 2;

    public int RetryBaseDelayMilliseconds { get; init; } = 250;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(RetryBaseDelayMilliseconds);

    public int BoundedMaxContentCharacters => Math.Clamp(MaxContentCharacters, 1, MaximumContentCharactersLimit);

    public int BoundedMaxRetryAttempts => Math.Clamp(MaxRetryAttempts, 0, 5);

    public void Validate()
    {
        if (MaxContentCharacters is < 1 or > MaximumContentCharactersLimit)
        {
            throw new InvalidOperationException($"EuropePmcFullText:MaxContentCharacters must be between 1 and {MaximumContentCharactersLimit}.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("EuropePmcFullText:TimeoutSeconds must be between 1 and 120.");
        }

        if (MaxRetryAttempts is < 0 or > 5)
        {
            throw new InvalidOperationException("EuropePmcFullText:MaxRetryAttempts must be between 0 and 5.");
        }

        if (RetryBaseDelayMilliseconds is < 1 or > 60000)
        {
            throw new InvalidOperationException("EuropePmcFullText:RetryBaseDelayMilliseconds must be between 1 and 60000.");
        }
    }
}

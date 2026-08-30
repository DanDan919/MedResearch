namespace MedResearch.Infrastructure.Ai.OpenAI;

public sealed class OpenAIOptions
{
    public const string SectionName = "AI";

    public string Provider { get; init; } = "OpenAI";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";

    public string? Model { get; init; }

    public string? ApiKey { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxOutputTokens { get; init; } = 2_000;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300));

    public int BoundedMaxOutputTokens => Math.Clamp(MaxOutputTokens, 256, 8_000);
}

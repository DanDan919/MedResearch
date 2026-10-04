namespace MedResearch.Infrastructure.Ai.CodexCli;

public sealed class CodexCliOptions
{
    public const string SectionName = "AI:CodexCli";

    public string ExecutablePath { get; init; } = "codex";

    public string? Model { get; init; }

    public int TimeoutSeconds { get; init; } = 300;

    public int MaxPromptCharacters { get; init; } = 500_000;

    public string Sandbox { get; init; } = "read-only";

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? "codex-cli-default" : Model.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)
            || ExecutablePath.Contains('"')
            || ExecutablePath.Contains('\r')
            || ExecutablePath.Contains('\n'))
        {
            throw new InvalidOperationException("AI:CodexCli:ExecutablePath must be a non-empty executable path without quotes or newlines.");
        }

        if (TimeoutSeconds is < 10 or > 1_800)
        {
            throw new InvalidOperationException("AI:CodexCli:TimeoutSeconds must be between 10 and 1800.");
        }

        if (MaxPromptCharacters is < 1_000 or > 2_000_000)
        {
            throw new InvalidOperationException("AI:CodexCli:MaxPromptCharacters must be between 1000 and 2000000.");
        }

        if (!string.Equals(Sandbox, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AI:CodexCli:Sandbox must remain read-only.");
        }
    }
}

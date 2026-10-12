namespace MedResearch.Application.Research.Ai;

public sealed record StructuredLlmRequest(
    string PromptVersion,
    string SystemPrompt,
    string UserPrompt,
    StructuredOutputSchema OutputSchema,
    StructuredLlmLimits? Limits = null);

public sealed record StructuredLlmLimits(int MaxOutputTokens, int MaxResponseBytes, int TimeoutSeconds)
{
    public void Validate()
    {
        if (MaxOutputTokens is < 256 or > 8000 || MaxResponseBytes is < 4096 or > 2_000_000 || TimeoutSeconds is < 1 or > 300)
            throw new ArgumentException("Invalid structured generation limits.");
    }
}

public sealed record StructuredOutputSchema(
    string Name,
    string JsonSchema);

public sealed record StructuredGenerationResult<T>(
    T Value,
    StructuredLlmProviderMetadata Metadata);

public sealed record StructuredLlmProviderMetadata(
    string Provider,
    string Model,
    string? ResponseId,
    DateTimeOffset GeneratedAt);

namespace MedResearch.Application.Research.Ai;

public sealed record StructuredLlmRequest(
    string PromptVersion,
    string SystemPrompt,
    string UserPrompt,
    StructuredOutputSchema OutputSchema);

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

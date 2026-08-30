namespace MedResearch.Application.Research.Planning;

public sealed record ResearchPlanDraft(
    string? OriginalQuestion,
    string? Population,
    string? ExposureOrIntervention,
    string? Comparator,
    IReadOnlyCollection<string>? Outcomes,
    IReadOnlyCollection<string>? PreferredStudyTypes,
    IReadOnlyCollection<string>? SearchQueries,
    IReadOnlyCollection<string>? ExclusionHints);

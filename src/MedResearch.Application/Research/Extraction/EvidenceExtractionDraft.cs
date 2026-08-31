using System.Text.Json.Serialization;

namespace MedResearch.Application.Research.Extraction;

public sealed record EvidenceExtractionDraft(
    [property: JsonPropertyName("findings")] IReadOnlyCollection<EvidenceFindingDraft>? Findings);

public sealed record EvidenceFindingDraft(
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("resultSummary")] string? ResultSummary,
    [property: JsonPropertyName("supportingText")] string? SupportingText,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("population")] string? Population,
    [property: JsonPropertyName("exposureOrIntervention")] string? ExposureOrIntervention,
    [property: JsonPropertyName("comparator")] string? Comparator,
    [property: JsonPropertyName("studyDesign")] string? StudyDesign,
    [property: JsonPropertyName("sampleSize")] int? SampleSize,
    [property: JsonPropertyName("effectMeasure")] string? EffectMeasure,
    [property: JsonPropertyName("effectValue")] decimal? EffectValue,
    [property: JsonPropertyName("confidenceIntervalLower")] decimal? ConfidenceIntervalLower,
    [property: JsonPropertyName("confidenceIntervalUpper")] decimal? ConfidenceIntervalUpper,
    [property: JsonPropertyName("pValue")] decimal? PValue);

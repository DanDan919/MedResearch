using System.Text.Json.Serialization;

namespace MedResearch.Application.Research.Evaluation;

public sealed record EvidenceEvaluationDraft(
    [property: JsonPropertyName("researchRunId")] string? ResearchRunId,
    [property: JsonPropertyName("studyId")] string? StudyId,
    [property: JsonPropertyName("studyDesign")] string? StudyDesign,
    [property: JsonPropertyName("sampleInformation")] string? SampleInformation,
    [property: JsonPropertyName("comparatorPresence")] string? ComparatorPresence,
    [property: JsonPropertyName("comparatorDescription")] string? ComparatorDescription,
    [property: JsonPropertyName("randomization")] string? Randomization,
    [property: JsonPropertyName("blinding")] string? Blinding,
    [property: JsonPropertyName("allocationConcealment")] string? AllocationConcealment,
    [property: JsonPropertyName("attritionMissingData")] string? AttritionMissingData,
    [property: JsonPropertyName("precision")] string? Precision,
    [property: JsonPropertyName("directness")] string? Directness,
    [property: JsonPropertyName("overallConfidence")] string? OverallConfidence,
    [property: JsonPropertyName("rationale")] string? Rationale,
    [property: JsonPropertyName("reportingLimitations")] IReadOnlyCollection<string>? ReportingLimitations,
    [property: JsonPropertyName("authorReportedLimitations")] IReadOnlyCollection<string>? AuthorReportedLimitations,
    [property: JsonPropertyName("qualityScore")] decimal? QualityScore = null);

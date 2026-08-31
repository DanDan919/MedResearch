using System.Text.Json;
using MedResearch.Application.Research.Ai;

namespace MedResearch.Application.Research.Extraction;

public static class EvidenceExtractionPrompt
{
    public const string Version = "evidence-extractor-v1";

    public static StructuredOutputSchema OutputSchema { get; } = new(
        "evidence_extraction",
        JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "findings" },
            properties = new
            {
                findings = new
                {
                    description = "Zero to twelve source-grounded evidence findings from the supplied abstract only.",
                    type = "array",
                    maxItems = 12,
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "outcome",
                            "resultSummary",
                            "supportingText",
                            "direction",
                            "population",
                            "exposureOrIntervention",
                            "comparator",
                            "studyDesign",
                            "sampleSize",
                            "effectMeasure",
                            "effectValue",
                            "confidenceIntervalLower",
                            "confidenceIntervalUpper",
                            "pValue"
                        },
                        properties = new
                        {
                            outcome = NullableString("Reported outcome or endpoint, otherwise null.", 300),
                            resultSummary = NullableString("Concise reported result for this outcome, otherwise null.", 800),
                            supportingText = NullableString("Verbatim excerpt from the supplied abstract supporting this finding.", 1000),
                            direction = new
                            {
                                type = new[] { "string", "null" },
                                @enum = new object?[] { "Positive", "Negative", "NoClearEffect", "Mixed", "NotReported", null }
                            },
                            population = NullableString("Reported population only, otherwise null.", 300),
                            exposureOrIntervention = NullableString("Reported exposure or intervention only, otherwise null.", 300),
                            comparator = NullableString("Reported comparator only, otherwise null.", 300),
                            studyDesign = new
                            {
                                type = new[] { "string", "null" },
                                @enum = new object?[]
                                {
                                    "randomized controlled trial",
                                    "controlled trial",
                                    "cohort study",
                                    "case-control study",
                                    "cross-sectional study",
                                    "systematic review",
                                    "meta-analysis",
                                    "observational study",
                                    "experimental study",
                                    "qualitative study",
                                    "review",
                                    "case report",
                                    "other",
                                    null
                                }
                            },
                            sampleSize = new { type = new[] { "integer", "null" }, minimum = 1 },
                            effectMeasure = NullableString("Reported effect measure label only, otherwise null.", 100),
                            effectValue = new { type = new[] { "number", "null" } },
                            confidenceIntervalLower = new { type = new[] { "number", "null" } },
                            confidenceIntervalUpper = new { type = new[] { "number", "null" } },
                            pValue = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 }
                        }
                    }
                }
            }
        }));

    public static EvidenceExtractionPromptText Create(EvidenceExtractionStudyContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ResearchQuestion);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Title);

        return new EvidenceExtractionPromptText(
            """
            You are a structured evidence extraction component for MedResearch.
            Extract only findings that are explicitly reported in the supplied PubMed title, abstract, and metadata.
            The LLM is not a scientific source. Do not add background knowledge, causal interpretation, clinical advice, diagnoses, treatments, or conclusions beyond the supplied source text.
            Use null for absent data. Do not guess missing sample sizes, effect sizes, confidence intervals, p-values, study designs, comparators, populations, or effect directions.
            supportingText must be a short verbatim excerpt from the supplied abstract. Do not paraphrase supportingText.
            Prefer reported findings only. If a direction is not explicitly supported, use NotReported rather than inferring no effect.
            Return only the strict structured object requested by the schema.
            """,
            $"""
            Prompt version: {Version}
            Source scope: abstract-level metadata only. Full text is not available.

            Research question:
            {context.ResearchQuestion}

            Bounded plan context:
            Population: {context.Plan?.Population ?? "null"}
            ExposureOrIntervention: {context.Plan?.ExposureOrIntervention ?? "null"}
            Comparator: {context.Plan?.Comparator ?? "null"}
            Outcomes: {Join(context.Plan?.Outcomes)}
            PreferredStudyTypes: {Join(context.Plan?.PreferredStudyTypes)}
            ExclusionHints: {Join(context.Plan?.ExclusionHints)}

            Authoritative study metadata:
            StudyId: {context.StudyId}
            Title: {context.Title}
            PMID: {context.Pmid ?? "null"}
            DOI: {context.Doi ?? "null"}
            Journal: {context.Journal ?? "null"}
            PublicationDate: {context.PublicationDate?.ToString("yyyy-MM-dd") ?? "null"}
            PublicationTypes: {Join(context.PublicationTypes)}
            Authors: {Join(context.Authors)}
            Source: {context.Source}

            Supplied abstract:
            {context.Abstract}
            """);
    }

    private static object NullableString(string description, int maxLength)
    {
        return new
        {
            description,
            type = new[] { "string", "null" },
            maxLength
        };
    }

    private static string Join(IReadOnlyCollection<string>? values)
    {
        return values is null || values.Count == 0 ? "[]" : string.Join("; ", values);
    }
}

public sealed record EvidenceExtractionPromptText(string SystemPrompt, string UserPrompt);

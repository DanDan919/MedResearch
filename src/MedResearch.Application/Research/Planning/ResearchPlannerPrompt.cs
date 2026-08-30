using System.Text.Json;
using MedResearch.Application.Research.Ai;

namespace MedResearch.Application.Research.Planning;

public static class ResearchPlannerPrompt
{
    public const string Version = "research-planner-v1";

    public static StructuredOutputSchema OutputSchema { get; } = new(
        "research_plan",
        JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "originalQuestion",
                "population",
                "exposureOrIntervention",
                "comparator",
                "outcomes",
                "preferredStudyTypes",
                "searchQueries",
                "exclusionHints"
            },
            properties = new
            {
                originalQuestion = NullableString("Exact submitted research question copied verbatim.", 2000),
                population = NullableString("Population or context when responsibly inferable, otherwise null."),
                exposureOrIntervention = NullableString("Exposure, intervention, condition, or phenomenon when inferable, otherwise null."),
                comparator = NullableString("Comparator when inferable, otherwise null."),
                outcomes = StringArray("Outcome concepts to search for; empty array when absent."),
                preferredStudyTypes = StringArray("Preferred study designs using the allowed labels in the prompt; empty array when absent."),
                searchQueries = SearchQueryArray("One to five conservative scientific database search queries. Do not include PMID, DOI, or invented titles."),
                exclusionHints = StringArray("Concepts that may be excluded later; empty array when absent.")
            }
        }));

    public static ResearchPlannerPromptText Create(string researchQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchQuestion);

        return new ResearchPlannerPromptText(
            """
            You are a scientific research planning component for MedResearch.
            Your only task is to decompose the submitted research question and propose conservative literature search queries.
            You are not a scientific authority and you must not synthesize evidence or make medical recommendations.
            Treat uncertain or absent PICO-like components as null or empty arrays instead of inventing details.
            Do not output PMIDs, DOIs, paper titles, authors, effect sizes, sample sizes, confidence intervals, p-values, evidence grades, diagnoses, treatments, or conclusions.
            Preferred study types must use only these labels when relevant: randomized controlled trial, controlled trial, cohort study, case-control study, cross-sectional study, systematic review, meta-analysis, observational study, experimental study, qualitative study, review, other.
            Return only the strict structured object requested by the schema.
            """,
            $"""
            Prompt version: {Version}
            Submitted research question:
            {researchQuestion}

            Copy the submitted research question exactly into originalQuestion. Generate one to five bounded search queries suitable for PubMed-style scientific retrieval.
            """);
    }

    private static object NullableString(string description, int maxLength = 500)
    {
        return new
        {
            description,
            type = new[] { "string", "null" },
            maxLength
        };
    }

    private static object StringArray(string description)
    {
        return ArraySchema(description, 10, 500);
    }

    private static object SearchQueryArray(string description)
    {
        return ArraySchema(description, 5, 300);
    }

    private static object ArraySchema(string description, int maxItems, int maxLength)
    {
        return new
        {
            description,
            type = "array",
            maxItems,
            items = new
            {
                type = "string",
                maxLength
            }
        };
    }
}

public sealed record ResearchPlannerPromptText(string SystemPrompt, string UserPrompt);


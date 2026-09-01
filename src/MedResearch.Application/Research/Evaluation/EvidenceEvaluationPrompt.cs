using System.Text.Json;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Evaluation;

public static class EvidenceEvaluationPrompt
{
    public const string Version = "evidence-evaluator-v1";

    public static StructuredOutputSchema OutputSchema { get; } = new(
        "evidence_evaluation",
        JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "researchRunId",
                "studyId",
                "studyDesign",
                "sampleInformation",
                "comparatorPresence",
                "comparatorDescription",
                "randomization",
                "blinding",
                "allocationConcealment",
                "attritionMissingData",
                "precision",
                "directness",
                "overallConfidence",
                "rationale",
                "reportingLimitations",
                "authorReportedLimitations"
            },
            properties = new
            {
                researchRunId = NullableString("Copy the supplied researchRunId exactly.", 64),
                studyId = NullableString("Copy the supplied studyId exactly.", 64),
                studyDesign = EnumString("Study design classification.", StudyDesignValues()),
                sampleInformation = EnumString("Sample information assessment.", AssessmentValues()),
                comparatorPresence = EnumString("Comparator/control presence.", ComparatorValues()),
                comparatorDescription = NullableString("Reported comparator description only; null if absent.", 300),
                randomization = EnumString("Randomization signal only when relevant and supported.", AssessmentValues()),
                blinding = EnumString("Blinding signal only when relevant and supported.", AssessmentValues()),
                allocationConcealment = EnumString("Allocation concealment signal only when relevant and supported.", AssessmentValues()),
                attritionMissingData = EnumString("Attrition or missing-data signal only when supported.", AssessmentValues()),
                precision = EnumString("Precision/uncertainty signal from reported quantitative information.", AssessmentValues()),
                directness = EnumString("Directness relative to the current research question.", DirectnessValues()),
                overallConfidence = EnumString("Internal MedResearch methodological confidence category, not GRADE.", ConfidenceValues()),
                rationale = NullableString("Concise source-aware rationale. Do not include full abstract text.", 1000),
                reportingLimitations = StringArray("Limitations of available MedResearch representation/source scope, not study misconduct.", 12, 300),
                authorReportedLimitations = StringArray("Only author/source-reported limitations explicitly present in source text or evidence excerpts.", 8, 300)
            }
        }));

    public static EvidenceEvaluationPromptText Create(EvaluationStudyContext context, EvidenceEvaluationSignalSet signals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ResearchQuestion);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Title);

        return new EvidenceEvaluationPromptText(
            """
            You are a structured methodological evidence evaluation component for MedResearch.
            Use only the supplied MedResearch context. Do not use external knowledge about the paper.
            Distinguish source absence from methodological weakness. Not reported in the abstract means Unknown or InsufficientSource, not Poor.
            Do not invent randomization, blinding, allocation concealment, dropout rates, statistical power, confounding, single-center status, or author limitations.
            Do not assign numeric quality scores. Do not claim formal GRADE, Cochrane RoB 2, ROBINS-I, AMSTAR-2, Newcastle-Ottawa, or any validated framework result.
            Statistical significance is not study quality. p < 0.05 is not high quality; p > 0.05 is not no effect.
            Use NotApplicable when a domain conceptually does not apply. Use InsufficientSource when the source scope is inadequate. Use Unknown when the value cannot be determined from supplied material.
            Return only the strict structured object requested by the schema.
            """,
            $"""
            Prompt version: {Version}
            researchRunId: {context.ResearchRunId}
            studyId: {context.StudyId}
            sourceScope: {context.SourceScope}

            Research question:
            {context.ResearchQuestion}

            Bounded plan context:
            Population: {context.Plan?.Population ?? "null"}
            ExposureOrIntervention: {context.Plan?.ExposureOrIntervention ?? "null"}
            Comparator: {context.Plan?.Comparator ?? "null"}
            Outcomes: {Join(context.Plan?.Outcomes)}
            PreferredStudyTypes: {Join(context.Plan?.PreferredStudyTypes)}
            ExclusionHints: {Join(context.Plan?.ExclusionHints)}

            Study metadata:
            Title: {context.Title}
            PMID: {context.Pmid ?? "null"}
            DOI: {context.Doi ?? "null"}
            Journal: {context.Journal ?? "null"}
            PublicationDate: {context.PublicationDate?.ToString("yyyy-MM-dd") ?? "null"}
            PublicationTypes: {Join(context.PublicationTypes)}
            Authors: {Join(context.Authors)}
            Source: {context.Source}

            Deterministic signals:
            EvidenceCount: {signals.EvidenceCount}
            MetadataStudyDesignHint: {signals.MetadataStudyDesignHint}
            HasSampleSize: {signals.HasSampleSize}
            HasEffectEstimate: {signals.HasEffectEstimate}
            HasConfidenceInterval: {signals.HasConfidenceInterval}
            HasPValue: {signals.HasPValue}
            HasComparator: {signals.HasComparator}
            ReportingLimitations: {Join(signals.ReportingLimitations)}

            Grounded evidence findings considered:
            {JoinEvidence(context.Evidence)}

            Abstract/source text for grounding methodological claims when needed:
            {context.Abstract ?? "null"}
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

    private static object EnumString(string description, object[] values)
    {
        return new
        {
            description,
            type = "string",
            @enum = values
        };
    }

    private static object StringArray(string description, int maxItems, int maxLength)
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

    private static object[] AssessmentValues()
    {
        return Enum.GetNames<MethodologicalAssessmentState>().Cast<object>().ToArray();
    }

    private static object[] ComparatorValues()
    {
        return Enum.GetNames<ComparatorPresence>().Cast<object>().ToArray();
    }

    private static object[] ConfidenceValues()
    {
        return Enum.GetNames<MethodologicalConfidence>().Cast<object>().ToArray();
    }

    private static object[] DirectnessValues()
    {
        return Enum.GetNames<DirectnessRating>().Cast<object>().ToArray();
    }

    private static object[] StudyDesignValues()
    {
        return Enum.GetNames<StudyDesignClassification>().Cast<object>().ToArray();
    }

    private static string Join(IReadOnlyCollection<string>? values)
    {
        return values is null || values.Count == 0 ? "[]" : string.Join("; ", values);
    }

    private static string JoinEvidence(IReadOnlyCollection<EvaluationEvidenceContext> evidence)
    {
        if (evidence.Count == 0)
        {
            return "[]";
        }

        return string.Join("\n---\n", evidence.Select(item =>
            $"EvidenceId: {item.EvidenceId}\nOutcome: {item.Outcome}\nResultSummary: {item.ResultSummary}\nDirection: {item.Direction}\nPopulation: {item.Population ?? "null"}\nExposureOrIntervention: {item.ExposureOrIntervention ?? "null"}\nComparator: {item.Comparator ?? "null"}\nStudyDesign: {item.StudyDesign ?? "null"}\nSampleSize: {item.SampleSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}\nEffectMeasure: {item.EffectMeasure ?? "null"}\nEffectValue: {item.EffectValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}\nConfidenceInterval: {item.ConfidenceIntervalLower?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"} to {item.ConfidenceIntervalUpper?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}\nPValue: {item.PValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}\nSupportingText: {item.SupportingText}"));
    }
}

public sealed record EvidenceEvaluationPromptText(string SystemPrompt, string UserPrompt);

using System.Globalization;
using System.Text.Json;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public static class ResearchSynthesisPrompt
{
    public const string Version = "research-synthesizer-v1";

    public static StructuredOutputSchema OutputSchema { get; } = new(
        "research_report",
        JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "reportStatus",
                "insufficientEvidenceReason",
                "executiveSummary",
                "evidenceSummary",
                "conflictSummary",
                "limitationsSummary",
                "conclusion",
                "synthesisConfidence",
                "claims"
            },
            properties = new
            {
                reportStatus = EnumString("Report status.", Enum.GetNames<ResearchReportStatus>()),
                insufficientEvidenceReason = NullableEnumString("Reason when reportStatus is InsufficientEvidence, otherwise null.", Enum.GetNames<ResearchReportInsufficientEvidenceReason>()),
                executiveSummary = NullableString("Brief synthesis summary using only supplied evidence.", 2000),
                evidenceSummary = NullableString("Qualitative summary of supplied evidence findings.", 2500),
                conflictSummary = NullableString("Summary of conflicting or non-conflicting evidence.", 1500),
                limitationsSummary = NullableString("Limitations from source coverage, extraction/evaluation state, and supplied evidence.", 2000),
                conclusion = NullableString("Cautious conclusion traceable to conclusion claims.", 1500),
                synthesisConfidence = EnumString("Internal MedResearch synthesis confidence, not GRADE.", Enum.GetNames<SynthesisConfidence>()),
                claims = new
                {
                    type = "array",
                    maxItems = 25,
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "type", "direction", "text", "evidenceIds" },
                        properties = new
                        {
                            type = EnumString("Claim type.", Enum.GetNames<ResearchReportClaimType>()),
                            direction = EnumString("Structured direction for deterministic support validation.", Enum.GetNames<ResearchReportClaimDirection>()),
                            text = NullableString("Substantive claim text using only supplied evidence.", 800),
                            evidenceIds = StringArray("Evidence ids from the supplied synthesis context supporting this claim.", 12, 64)
                        }
                    }
                }
            }
        }));

    public static ResearchSynthesisPromptText Create(SynthesisContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ResearchQuestion);

        return new ResearchSynthesisPromptText(
            """
            You are a structured evidence synthesis component for MedResearch.
            Use only the supplied SynthesisContext. Do not use outside scientific knowledge, remembered papers, invented studies, invented statistics, invented PMIDs, or invented DOIs.
            Every substantive scientific claim must cite one or more supplied EvidenceId values. Do not cite StudyId, PMID, or DOI as model-generated authority.
            Preserve conflicting evidence. Do not force a single winning direction because one side has more studies.
            Do not vote-count studies into certainty. Direction counts are descriptive context only, not statistical weights.
            Do not perform meta-analysis or pool odds ratios, risk ratios, mean differences, correlations, p-values, confidence intervals, or effect sizes.
            Distinguish source-supported methodological concerns from Unknown, InsufficientSource, and NotApplicable evaluation states.
            Do not claim formal GRADE, Cochrane RoB, ROBINS-I, AMSTAR-2, NOS, diagnosis, treatment recommendation, or prescription.
            Use calibrated language and return only the strict structured object requested by the schema.
            """,
            $"""
            Prompt version: {Version}
            researchRunId: {context.ResearchRunId}

            Research question:
            {context.ResearchQuestion}

            Plan summary:
            Population: {context.Plan?.Population ?? "null"}
            ExposureOrIntervention: {context.Plan?.ExposureOrIntervention ?? "null"}
            Comparator: {context.Plan?.Comparator ?? "null"}
            Outcomes: {Join(context.Plan?.Outcomes)}
            PreferredStudyTypes: {Join(context.Plan?.PreferredStudyTypes)}
            SearchQueries: {Join(context.Plan?.SearchQueries)}
            ExclusionHints: {Join(context.Plan?.ExclusionHints)}

            Corpus statistics:
            DiscoveredStudyCount: {context.Statistics.DiscoveredStudyCount}
            ExtractedStudyCount: {context.Statistics.ExtractedStudyCount}
            EvaluatedStudyCount: {context.Statistics.EvaluatedStudyCount}
            EvidenceFindingCount: {context.Statistics.EvidenceFindingCount}
            IncludedStudyCount: {context.Statistics.IncludedStudyCount}
            IncludedEvidenceFindingCount: {context.Statistics.IncludedEvidenceFindingCount}
            StudiesWithNoExtractableEvidence: {context.Statistics.StudiesWithNoExtractableEvidence}
            StudiesWithInsufficientEvaluationSource: {context.Statistics.StudiesWithInsufficientEvaluationSource}

            Source coverage:
            SearchedSources: {Join(context.SourceCoverage.SearchedSources)}
            UsesAbstractLevelEvidenceOnly: {context.SourceCoverage.UsesAbstractLevelEvidenceOnly}
            IncludesFullTextEvidence: {context.SourceCoverage.IncludesFullTextEvidence}
            EvidenceTruncated: {context.SourceCoverage.EvidenceTruncated}
            ExecutedSearchCount: {context.SourceCoverage.ExecutedSearchCount}

            Outcome direction summaries:
            {JoinOutcomes(context.OutcomeDirectionSummaries)}

            Deterministic limitations that must be respected:
            {Join(context.DeterministicLimitations)}

            Included studies, evaluations, and evidence:
            {JoinStudies(context.Studies)}
            """);
    }

    private static object NullableString(string description, int maxLength)
    {
        return new { description, type = new[] { "string", "null" }, maxLength };
    }

    private static object EnumString(string description, string[] values)
    {
        return new { description, type = "string", @enum = values.Cast<object>().ToArray() };
    }

    private static object NullableEnumString(string description, string[] values)
    {
        return new { description, type = new[] { "string", "null" }, @enum = values.Cast<object?>().Concat([null]).ToArray() };
    }

    private static object StringArray(string description, int maxItems, int maxLength)
    {
        return new
        {
            description,
            type = "array",
            maxItems,
            items = new { type = "string", maxLength }
        };
    }

    private static string Join(IReadOnlyCollection<string>? values)
    {
        return values is null || values.Count == 0 ? "[]" : string.Join("; ", values);
    }

    private static string JoinOutcomes(IReadOnlyCollection<SynthesisOutcomeDirectionSummary> outcomes)
    {
        return outcomes.Count == 0
            ? "[]"
            : string.Join("\n", outcomes.Select(outcome => $"Outcome: {outcome.Outcome}; Positive: {outcome.PositiveCount}; Negative: {outcome.NegativeCount}; NoClearEffect: {outcome.NoClearEffectCount}; Mixed: {outcome.MixedCount}; NotReported: {outcome.NotReportedCount}; ConflictStatus: {outcome.ConflictStatus}"));
    }

    private static string JoinStudies(IReadOnlyCollection<SynthesisStudyContext> studies)
    {
        if (studies.Count == 0)
        {
            return "[]";
        }

        return string.Join("\n---\n", studies.Select(study =>
            $"StudyId: {study.StudyId}\nTitle: {study.Title}\nPMID: {study.Pmid ?? "null"}\nPMCID: {study.Pmcid ?? "null"}\nDOI: {study.Doi ?? "null"}\nJournal: {study.Journal ?? "null"}\nPublicationDate: {study.PublicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null"}\nPublicationTypes: {Join(study.PublicationTypes)}\nSource: {study.Source}\nEvaluation: {FormatEvaluation(study.Evaluation)}\nEvidence:\n{JoinEvidence(study.Evidence)}"));
    }

    private static string FormatEvaluation(SynthesisEvaluationContext? evaluation)
    {
        if (evaluation is null)
        {
            return "null";
        }

        return $"Status={evaluation.Status}; StudyDesign={evaluation.StudyDesign}; SampleInformation={evaluation.SampleInformation}; ComparatorPresence={evaluation.ComparatorPresence}; Randomization={evaluation.Randomization}; Blinding={evaluation.Blinding}; AllocationConcealment={evaluation.AllocationConcealment}; AttritionMissingData={evaluation.AttritionMissingData}; Precision={evaluation.Precision}; Directness={evaluation.Directness}; OverallConfidence={evaluation.OverallConfidence}; UnknownDomainCount={evaluation.UnknownDomainCount}; InsufficientSourceDomainCount={evaluation.InsufficientSourceDomainCount}; ReportingLimitations={Join(evaluation.ReportingLimitations)}";
    }

    private static string JoinEvidence(IReadOnlyCollection<SynthesisEvidenceContext> evidence)
    {
        if (evidence.Count == 0)
        {
            return "[]";
        }

        return string.Join("\n", evidence.Select(item =>
            $"EvidenceId: {item.EvidenceId}; Outcome: {item.Outcome}; Direction: {item.Direction}; ResultSummary: {item.ResultSummary}; Population: {item.Population ?? "null"}; ExposureOrIntervention: {item.ExposureOrIntervention ?? "null"}; Comparator: {item.Comparator ?? "null"}; StudyDesign: {item.StudyDesign ?? "null"}; SampleSize: {item.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "null"}; EffectMeasure: {item.EffectMeasure ?? "null"}; EffectValue: {item.EffectValue?.ToString(CultureInfo.InvariantCulture) ?? "null"}; ConfidenceInterval: {item.ConfidenceIntervalLower?.ToString(CultureInfo.InvariantCulture) ?? "null"} to {item.ConfidenceIntervalUpper?.ToString(CultureInfo.InvariantCulture) ?? "null"}; PValue: {item.PValue?.ToString(CultureInfo.InvariantCulture) ?? "null"}; SupportingText: {item.SupportingText}"));
    }
}

public sealed record ResearchSynthesisPromptText(string SystemPrompt, string UserPrompt);

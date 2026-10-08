using System.Globalization;
using System.Text.Json;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public static class ResearchSynthesisPrompt
{
    public const string Version = "research-synthesizer-v3-structured-claims";

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
                "synthesisConfidence",
                "claims"
            },
            properties = new
            {
                reportStatus = EnumString("Report status.", Enum.GetNames<ResearchReportStatus>()),
                insufficientEvidenceReason = NullableEnumString("Reason when reportStatus is InsufficientEvidence, otherwise null.", Enum.GetNames<ResearchReportInsufficientEvidenceReason>()),
                synthesisConfidence = EnumString("Internal MedResearch synthesis confidence, not GRADE.", Enum.GetNames<SynthesisConfidence>()),
                claims = new
                {
                    type = "array",
                    maxItems = 25,
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "type", "direction", "kind", "outcome", "population", "exposureOrIntervention", "comparator", "timepoint", "evidenceIds", "numericEvidenceId", "quantitativeArtifactId", "statistic" },
                        properties = new
                        {
                            type = EnumString("Claim type.", Enum.GetNames<ResearchReportClaimType>()),
                            direction = EnumString("Structured direction for deterministic support validation.", Enum.GetNames<ResearchReportClaimDirection>()),
                            kind = EnumString("Closed scientific claim semantics.", Enum.GetNames<ResearchClaimKind>()),
                            outcome = NullableString("Exact cited Evidence outcome; null only for insufficiency.", 512),
                            population = NullableString("Exact cited Evidence population; preserve null.", 512),
                            exposureOrIntervention = NullableString("Exact cited Evidence intervention/exposure; preserve null.", 512),
                            comparator = NullableString("Exact cited Evidence comparator; preserve null.", 512),
                            timepoint = NullableString("Exact cited Evidence timepoint; preserve null.", 512),
                            evidenceIds = StringArray("Exact supplied support subset. For pooled claims use all and only artifact contribution Evidence IDs.", 12, 64),
                            numericEvidenceId = NullableString("Single Evidence ID for ReportedStudyResult, otherwise null.", 64),
                            quantitativeArtifactId = NullableString("Exact persisted artifact ID for QuantitativeSynthesis, otherwise null.", 64),
                            statistic = NullableEnumString("Statistic selector, not a copied number. Null for qualitative/mixed/insufficiency claims.", Enum.GetNames<ResearchClaimStatistic>())
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
            Propose structured claim semantics only. Never return claim text, numeric values, or free report sections. MedResearch validates references and renders all authoritative sentences/numbers deterministically.
            Copy each outcome/population/intervention/comparator/timepoint exactly from all cited Evidence; null is missing context, not a wildcard. No paraphrase or generalization.
            QualitativeEffect requires every cited direction to agree. MixedEvidence is required for differing directions, including Positive plus NoClearEffect. Use exact cited subsets, never corpus-wide efficacy wording.
            Numeric claims use direction NotApplicable: a number is not proof of clinical benefit, causality or clinical significance. Select the statistic and current-run persisted ArtifactId or NumericEvidenceId; never calculate or copy values.
            InsufficientEvidence is a metadata claim with null scope/references/statistic, empty evidenceIds and NotApplicable direction; it never means no effect. Completed reports require a Conclusion role. Do not duplicate the same semantics under multiple roles.
            Use only the supplied SynthesisContext. Do not use outside scientific knowledge, remembered papers, invented studies, invented statistics, invented PMIDs, or invented DOIs.
            Raw extraction ResultSummary is excluded. Only the explicitly grounded structured fields are numeric assertions for an Evidence finding. SupportingText is a source quotation, not permission to import another result or an unsupported statistic. Missing or unverified values remain unknown.
            Every substantive scientific claim must cite one or more supplied EvidenceId values. Do not cite StudyId, PMID, or DOI as model-generated authority.
            Preserve conflicting evidence. Do not force a single winning direction because one side has more studies.
            Do not vote-count studies into certainty. Direction counts are descriptive context only, not statistical weights.
            Do not calculate your own meta-analysis, pooled effect, heterogeneity statistic, between-study variance, tau-squared, p-value, confidence interval, prediction interval, random-effects weight, random-effects pooled estimate, HKSJ inference, or effect size. If deterministic quantitative syntheses are supplied, preserve their fixed-effect, heterogeneity, tau-squared, random-effects Wald, HKSJ, and prediction-interval values and limitations exactly.
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

            Deterministic quantitative syntheses computed by MedResearch application code:
            {JoinQuantitativeSyntheses(context.QuantitativeSyntheses)}

            Persisted quantitative artifacts available for authoritative references (no artifact means no pooled claim):
            {JsonSerializer.Serialize(context.QuantitativeArtifacts ?? [], new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })}

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


    private static string JoinQuantitativeSyntheses(IReadOnlyCollection<SynthesisQuantitativeResultContext> syntheses)
    {
        if (syntheses.Count == 0)
        {
            return "[]";
        }

        return string.Join("\n", syntheses.Select(synthesis =>
            $"GroupKey: {synthesis.GroupKey}; Outcome: {synthesis.OutcomeGroupKey}; EffectMeasure: {synthesis.EffectMeasureType}; Method: {synthesis.Method}; AlgorithmVersion: {synthesis.AlgorithmVersion}; ConfidenceLevel: {synthesis.OutputConfidenceLevel.ToString(CultureInfo.InvariantCulture)}; AnalysisScaleEffect: {synthesis.AnalysisScaleEffect.ToString("G17", CultureInfo.InvariantCulture)}; AnalysisScaleSE: {synthesis.AnalysisScaleStandardError.ToString("G17", CultureInfo.InvariantCulture)}; AnalysisScaleCI: {synthesis.AnalysisScaleConfidenceIntervalLower.ToString("G17", CultureInfo.InvariantCulture)} to {synthesis.AnalysisScaleConfidenceIntervalUpper.ToString("G17", CultureInfo.InvariantCulture)}; ReportedScaleEffect: {synthesis.ReportedScaleEffect.ToString("G17", CultureInfo.InvariantCulture)}; ReportedScaleCI: {synthesis.ReportedScaleConfidenceIntervalLower.ToString("G17", CultureInfo.InvariantCulture)} to {synthesis.ReportedScaleConfidenceIntervalUpper.ToString("G17", CultureInfo.InvariantCulture)}; Heterogeneity: {FormatHeterogeneity(synthesis.HeterogeneityDiagnostics)}; BetweenStudyVariance: {FormatBetweenStudyVariance(synthesis.BetweenStudyVariance)}; RandomEffects: {FormatRandomEffects(synthesis.RandomEffects)}; UniqueStudyCount: {synthesis.UniqueStudyCount}; EvidenceCount: {synthesis.EvidenceCount}; EvidenceIds: {Join(synthesis.Contributions.Select(contribution => contribution.EvidenceId.ToString()).ToArray())}"));
    }


    private static string FormatRandomEffects(SynthesisRandomEffectsResultContext? result)
    {
        if (result is null)
        {
            return "null";
        }

        var tauSquared = result.TauSquared.HasValue
            ? result.TauSquared.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleEffect = result.AnalysisScaleEffect.HasValue
            ? result.AnalysisScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleVariance = result.AnalysisScaleVariance.HasValue
            ? result.AnalysisScaleVariance.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleStandardError = result.AnalysisScaleStandardError.HasValue
            ? result.AnalysisScaleStandardError.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleLower = result.AnalysisScaleConfidenceIntervalLower.HasValue
            ? result.AnalysisScaleConfidenceIntervalLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleUpper = result.AnalysisScaleConfidenceIntervalUpper.HasValue
            ? result.AnalysisScaleConfidenceIntervalUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedEffect = result.ReportedScaleEffect.HasValue
            ? result.ReportedScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedLower = result.ReportedScaleConfidenceIntervalLower.HasValue
            ? result.ReportedScaleConfidenceIntervalLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedUpper = result.ReportedScaleConfidenceIntervalUpper.HasValue
            ? result.ReportedScaleConfidenceIntervalUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var failures = result.FailureReasons.Count == 0
            ? "[]"
            : string.Join("; ", result.FailureReasons.Select(reason => reason.ToString()));
        var contributionWeights = result.Contributions.Count == 0
            ? "[]"
            : string.Join("; ", result.Contributions.Select(contribution => $"EvidenceId={contribution.EvidenceId}, StudyId={contribution.StudyId}, Weight={contribution.Weight.ToString("G17", CultureInfo.InvariantCulture)}, NormalizedWeight={contribution.NormalizedWeight.ToString("G17", CultureInfo.InvariantCulture)}"));

        return $"Status: {result.Status}; Method: {result.Method}; AlgorithmVersion: {result.AlgorithmVersion}; ConfidenceIntervalMethod: {result.ConfidenceIntervalMethod}; ConfidenceLevel: {result.OutputConfidenceLevel.ToString(CultureInfo.InvariantCulture)}; TauSquared: {tauSquared}; TauSquaredEstimator: {result.TauSquaredEstimator}; TauSquaredAlgorithmVersion: {result.TauSquaredAlgorithmVersion}; StudyCount: {result.StudyCount}; AnalysisScaleEffect: {analysisScaleEffect}; AnalysisScaleVariance: {analysisScaleVariance}; AnalysisScaleSE: {analysisScaleStandardError}; AnalysisScaleCI: {analysisScaleLower} to {analysisScaleUpper}; ReportedScaleEffect: {reportedEffect}; ReportedScaleCI: {reportedLower} to {reportedUpper}; HksjInference: {FormatHksjInference(result.HksjInference)}; PredictionInterval: {FormatPredictionInterval(result.PredictionInterval)}; ContributionWeights: {contributionWeights}; FailureReasons: {failures}";
    }

    private static string FormatHksjInference(SynthesisHksjInferenceContext? result)
    {
        if (result is null)
        {
            return "null";
        }

        var degreesOfFreedom = result.DegreesOfFreedom?.ToString(CultureInfo.InvariantCulture) ?? "null";
        var varianceAdjustment = result.VarianceAdjustment.HasValue
            ? result.VarianceAdjustment.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var criticalValue = result.CriticalValue.HasValue
            ? result.CriticalValue.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleEffect = result.AnalysisScaleEffect.HasValue
            ? result.AnalysisScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleVariance = result.AnalysisScaleVariance.HasValue
            ? result.AnalysisScaleVariance.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleStandardError = result.AnalysisScaleStandardError.HasValue
            ? result.AnalysisScaleStandardError.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleLower = result.AnalysisScaleConfidenceIntervalLower.HasValue
            ? result.AnalysisScaleConfidenceIntervalLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleUpper = result.AnalysisScaleConfidenceIntervalUpper.HasValue
            ? result.AnalysisScaleConfidenceIntervalUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedEffect = result.ReportedScaleEffect.HasValue
            ? result.ReportedScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedLower = result.ReportedScaleConfidenceIntervalLower.HasValue
            ? result.ReportedScaleConfidenceIntervalLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedUpper = result.ReportedScaleConfidenceIntervalUpper.HasValue
            ? result.ReportedScaleConfidenceIntervalUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var failures = result.FailureReasons.Count == 0
            ? "[]"
            : string.Join("; ", result.FailureReasons.Select(reason => reason.ToString()));

        return $"Status: {result.Status}; ConfidenceIntervalMethod: {result.ConfidenceIntervalMethod}; AlgorithmVersion: {result.AlgorithmVersion}; ConfidenceLevel: {result.OutputConfidenceLevel.ToString(CultureInfo.InvariantCulture)}; StudyCount: {result.StudyCount}; DegreesOfFreedom: {degreesOfFreedom}; VarianceAdjustment: {varianceAdjustment}; CriticalValue: {criticalValue}; AnalysisScaleEffect: {analysisScaleEffect}; AnalysisScaleVariance: {analysisScaleVariance}; AnalysisScaleSE: {analysisScaleStandardError}; AnalysisScaleCI: {analysisScaleLower} to {analysisScaleUpper}; ReportedScaleEffect: {reportedEffect}; ReportedScaleCI: {reportedLower} to {reportedUpper}; FailureReasons: {failures}";
    }

    private static string FormatPredictionInterval(SynthesisPredictionIntervalContext? result)
    {
        if (result is null)
        {
            return "null";
        }

        var degreesOfFreedom = result.DegreesOfFreedom?.ToString(CultureInfo.InvariantCulture) ?? "null";
        var tauSquared = result.TauSquared.HasValue
            ? result.TauSquared.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var summaryVariance = result.SummaryEffectVariance.HasValue
            ? result.SummaryEffectVariance.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var summaryStandardError = result.SummaryEffectStandardError.HasValue
            ? result.SummaryEffectStandardError.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var predictionVariance = result.PredictionVariance.HasValue
            ? result.PredictionVariance.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var predictionStandardError = result.PredictionStandardError.HasValue
            ? result.PredictionStandardError.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var criticalValue = result.CriticalValue.HasValue
            ? result.CriticalValue.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleEffect = result.AnalysisScaleEffect.HasValue
            ? result.AnalysisScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleLower = result.AnalysisScaleLower.HasValue
            ? result.AnalysisScaleLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var analysisScaleUpper = result.AnalysisScaleUpper.HasValue
            ? result.AnalysisScaleUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedEffect = result.ReportedScaleEffect.HasValue
            ? result.ReportedScaleEffect.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedLower = result.ReportedScaleLower.HasValue
            ? result.ReportedScaleLower.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var reportedUpper = result.ReportedScaleUpper.HasValue
            ? result.ReportedScaleUpper.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var failures = result.FailureReasons.Count == 0
            ? "[]"
            : string.Join("; ", result.FailureReasons.Select(reason => reason.ToString()));

        return $"Status: {result.Status}; Method: {result.Method}; AlgorithmVersion: {result.AlgorithmVersion}; ConfidenceLevel: {result.OutputConfidenceLevel.ToString(CultureInfo.InvariantCulture)}; StudyCount: {result.StudyCount}; DegreesOfFreedom: {degreesOfFreedom}; TauSquared: {tauSquared}; SummaryEffectVariance: {summaryVariance}; SummaryEffectSE: {summaryStandardError}; PredictionVariance: {predictionVariance}; PredictionSE: {predictionStandardError}; CriticalValue: {criticalValue}; AnalysisScaleEffect: {analysisScaleEffect}; AnalysisScalePredictionInterval: {analysisScaleLower} to {analysisScaleUpper}; ReportedScaleEffect: {reportedEffect}; ReportedScalePredictionInterval: {reportedLower} to {reportedUpper}; FailureReasons: {failures}";
    }

    private static string FormatHeterogeneity(SynthesisQuantitativeHeterogeneityDiagnosticsContext? diagnostics)
    {
        if (diagnostics is null)
        {
            return "null";
        }

        return $"AlgorithmVersion: {diagnostics.AlgorithmVersion}; CochransQ: {diagnostics.CochransQ.ToString("G17", CultureInfo.InvariantCulture)}; DegreesOfFreedom: {diagnostics.DegreesOfFreedom}; ISquared: {diagnostics.ISquared.ToString("G17", CultureInfo.InvariantCulture)}; StudyCount: {diagnostics.StudyCount}";
    }

    private static string FormatBetweenStudyVariance(SynthesisBetweenStudyVarianceContext? estimate)
    {
        if (estimate is null)
        {
            return "null";
        }

        var tauSquared = estimate.TauSquared.HasValue
            ? estimate.TauSquared.Value.ToString("G17", CultureInfo.InvariantCulture)
            : "null";
        var failureReason = estimate.FailureReason?.ToString() ?? "null";
        return $"AlgorithmVersion: {estimate.AlgorithmVersion}; Estimator: {estimate.Estimator}; Status: {estimate.Status}; TauSquared: {tauSquared}; StudyCount: {estimate.StudyCount}; Converged: {estimate.Converged}; IterationCount: {estimate.IterationCount}; FailureReason: {failureReason}";
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

        return string.Join("\n", evidence.Select(SynthesisEvidenceProjection.Create).Select(item =>
            $"EvidenceId: {item.EvidenceId}; Outcome: {item.Outcome}; Direction: {item.Direction}; Population: {item.Population ?? "null"}; ExposureOrIntervention: {item.ExposureOrIntervention ?? "null"}; Comparator: {item.Comparator ?? "null"}; Timepoint: {item.Timepoint ?? "null"}; StudyDesign: {item.StudyDesign ?? "null"}; SampleSize: {item.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "null"}; EffectMeasure: {item.EffectMeasure ?? "null"}; EffectValue: {item.EffectValue?.ToString(CultureInfo.InvariantCulture) ?? "null"}; ConfidenceInterval: {item.ConfidenceIntervalLower?.ToString(CultureInfo.InvariantCulture) ?? "null"} to {item.ConfidenceIntervalUpper?.ToString(CultureInfo.InvariantCulture) ?? "null"}; ConfidenceLevel: {item.ConfidenceLevel?.ToString(CultureInfo.InvariantCulture) ?? "null"}; ReportedStandardError: {item.ReportedStandardError?.ToString(CultureInfo.InvariantCulture) ?? "null"}; PValue: {item.PValueOperator ?? "null"} {item.PValue?.ToString(CultureInfo.InvariantCulture) ?? "null"}; SupportingText (source quotation, not additional assertions): {item.SupportingText}"));
    }

}

public sealed record ResearchSynthesisPromptText(string SystemPrompt, string UserPrompt);

using System.Globalization;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Synthesis;

namespace MedResearch.Application.Tests;

internal static class GroundedEvidenceFixture
{
    public static SynthesisEvidenceContext Create(SynthesisEvidenceContext evidence, Guid sourceId)
    {
        string Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "not reported";
        var text = $"{evidence.SampleSize} participants were randomized. In {evidence.Population}, {evidence.ExposureOrIntervention} versus {evidence.Comparator} at 12 weeks: {evidence.Outcome} {evidence.EffectMeasure} {Format(evidence.EffectValue)}";
        if (evidence.ConfidenceIntervalLower.HasValue && evidence.ConfidenceIntervalUpper.HasValue)
        {
            var level = evidence.ConfidenceLevel.HasValue ? Format(evidence.ConfidenceLevel * 100m) + "% " : "";
            text += $" ({level}CI {Format(evidence.ConfidenceIntervalLower)} to {Format(evidence.ConfidenceIntervalUpper)})";
        }
        if (evidence.ReportedStandardError.HasValue)
        {
            text += $", SE {Format(evidence.ReportedStandardError)}";
        }
        text += evidence.SupportingText.Contains("TRUNCATED", StringComparison.Ordinal) ? ". TRUNCATED" : ".";
        var anchor = new SourceAnchorResolver().Resolve(sourceId, text, text).Anchor!;
        var draft = new EvidenceFindingDraft(evidence.Outcome, evidence.ResultSummary, text, evidence.Direction.ToString(), evidence.Population,
            evidence.ExposureOrIntervention, evidence.Comparator, evidence.StudyDesign, evidence.SampleSize, evidence.EffectMeasure,
            evidence.EffectValue, evidence.ConfidenceIntervalLower, evidence.ConfidenceIntervalUpper, evidence.PValue, evidence.ConfidenceLevel,
            evidence.ReportedStandardError, evidence.PValueOperator, "12 weeks");
        return evidence with { SupportingText = text, Timepoint = "12 weeks", NumericGrounding = new SemanticNumericGroundingVerifier().Verify(draft, anchor).Facts };
    }
}

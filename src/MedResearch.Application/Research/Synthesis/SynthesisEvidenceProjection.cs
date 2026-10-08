using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

internal static class SynthesisEvidenceProjection
{
    public static SynthesisEvidenceContext Create(SynthesisEvidenceContext item)
    {
        var stored = item.NumericGrounding ?? [];
        var anchor = stored.FirstOrDefault(fact => fact.Anchor is not null)?.Anchor;
        var current = anchor is null || anchor.NormalizationVersion != SourceAnchorResolver.NormalizationVersion || !SourceAnchorIntegrity.IsValid(anchor)
            ? [] : new SemanticNumericGroundingVerifier().Verify(new(item.Outcome, item.ResultSummary, item.SupportingText, item.Direction.ToString(),
                item.Population, item.ExposureOrIntervention, item.Comparator, item.StudyDesign, item.SampleSize, item.EffectMeasure, item.EffectValue,
                item.ConfidenceIntervalLower, item.ConfidenceIntervalUpper, item.PValue, item.ConfidenceLevel, item.ReportedStandardError, item.PValueOperator, item.Timepoint), anchor).Facts;
        bool Verified(NumericGroundingField field) => stored.Count(fact => fact.Field == field) == 1
            && stored.Any(fact => fact.Field == field && fact.Status == NumericGroundingStatus.Verified && fact.Anchor == anchor)
            && current.Any(fact => fact.Field == field && fact.Status == NumericGroundingStatus.Verified);
        return item with
        {
            ResultSummary = "Raw extraction summary excluded; use grounded fields and the source quotation.",
            SampleSize = Verified(NumericGroundingField.SampleSize) ? item.SampleSize : null,
            EffectMeasure = Verified(NumericGroundingField.EffectMeasure) ? item.EffectMeasure : null,
            EffectValue = Verified(NumericGroundingField.EffectEstimate) ? item.EffectValue : null,
            ConfidenceIntervalLower = Verified(NumericGroundingField.ConfidenceInterval) ? item.ConfidenceIntervalLower : null,
            ConfidenceIntervalUpper = Verified(NumericGroundingField.ConfidenceInterval) ? item.ConfidenceIntervalUpper : null,
            ConfidenceLevel = Verified(NumericGroundingField.ConfidenceLevel) ? item.ConfidenceLevel : null,
            ReportedStandardError = Verified(NumericGroundingField.StandardError) ? item.ReportedStandardError : null,
            PValue = Verified(NumericGroundingField.PValue) ? item.PValue : null,
            PValueOperator = Verified(NumericGroundingField.PValue) ? item.PValueOperator : null
        };
    }
}

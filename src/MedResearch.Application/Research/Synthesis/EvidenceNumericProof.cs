using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

internal static class EvidenceNumericProof
{
    public static bool IsSourceMember(SourceAnchor anchor, SynthesisSourceMaterialSnapshot source, string supportingText)
    {
        if (anchor.SourceMaterialId != source.SourceMaterialId || !SourceAnchorIntegrity.IsValid(anchor)
            || anchor.NormalizationVersion != SourceAnchorResolver.NormalizationVersion || string.IsNullOrWhiteSpace(source.Content)
            || SourceMaterial.ComputeContentHash(source.Content) != source.ContentHash)
        {
            return false;
        }

        // Resolve again against authoritative content, including uniqueness/overlap.
        var resolved = new SourceAnchorResolver().Resolve(source.SourceMaterialId, source.Content, supportingText);
        return resolved.Status == NumericGroundingStatus.Verified && resolved.Anchor is not null
            && (anchor.LexicalText is null ? resolved.Anchor with { LexicalText = null } : resolved.Anchor) == anchor;
    }

    public static IReadOnlyCollection<NumericGroundingFact> Revalidate(SynthesisEvidenceContext evidence, SynthesisSourceMaterialSnapshot? source)
    {
        var stored = evidence.NumericGrounding ?? [];
        var anchor = stored.FirstOrDefault(fact => fact.Anchor is not null)?.Anchor;
        var scopeMatches = source is not null && (source.Type, evidence.SourceScope) is
            (SourceMaterialType.Abstract, EvidenceSourceScope.Abstract) or (SourceMaterialType.StructuredFullText, EvidenceSourceScope.StructuredFullText);
        if (source is null || !scopeMatches || source.StudyId != evidence.StudyId || anchor is null || !IsSourceMember(anchor, source, evidence.SupportingText))
        {
            return [];
        }

        var finding = new EvidenceFindingDraft(evidence.Outcome, evidence.ResultSummary, evidence.SupportingText, evidence.Direction.ToString(),
            evidence.Population, evidence.ExposureOrIntervention, evidence.Comparator, evidence.StudyDesign, evidence.SampleSize, evidence.EffectMeasure,
            evidence.EffectValue, evidence.ConfidenceIntervalLower, evidence.ConfidenceIntervalUpper, evidence.PValue, evidence.ConfidenceLevel,
            evidence.ReportedStandardError, evidence.PValueOperator, evidence.Timepoint);
        var current = new SemanticNumericGroundingVerifier().Verify(finding, anchor).Facts;
        return current.Select(fact =>
        {
            var originals = stored.Where(original => original.Field == fact.Field).ToArray();
            return originals.Length == 1 && originals[0].Status == NumericGroundingStatus.Verified && originals[0].Anchor == anchor
                ? fact
                : fact with { Status = NumericGroundingStatus.Unsupported, Reason = "Mandatory persisted proof is absent, duplicate or unverified." };
        }).ToArray();
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public static class StructuredResearchClaimRenderer
{
    public static string Render(ResearchClaimSemantics claim)
    {
        if (claim.Kind == ResearchClaimKind.InsufficientEvidence)
            return "Available validated Evidence was insufficient to support an effect conclusion. This is not evidence of no effect.";
        var scope = $"Outcome: {claim.Outcome}; population: {Known(claim.Population)}; intervention/exposure: {Known(claim.ExposureOrIntervention)}; comparator: {Known(claim.Comparator)}; timepoint: {Known(claim.Timepoint)}.";
        if (claim.Kind == ResearchClaimKind.MixedEvidence)
            return $"Cited Evidence reports mixed or differing directions, not a uniform effect. {scope}";
        if (claim.Kind == ResearchClaimKind.QualitativeEffect)
        {
            var direction = claim.Direction switch
            {
                ResearchReportClaimDirection.Positive => "a positive reported direction",
                ResearchReportClaimDirection.Negative => "a negative reported direction",
                ResearchReportClaimDirection.NoClearEffect => "no clear reported effect, not proof of no effect",
                ResearchReportClaimDirection.NotReported => "no reported direction",
                _ => throw new InvalidOperationException("Unsupported structured qualitative direction.")
            };
            return $"Within the cited Evidence, findings have {direction}. This does not establish causality or clinical significance. {scope}";
        }
        var number = claim.Numeric ?? throw new InvalidOperationException("Numeric claim requires an authoritative numeric snapshot.");
        var origin = claim.Kind == ResearchClaimKind.ReportedStudyResult ? "Reported study statistic" : claim.Statistic switch
        {
            ResearchClaimStatistic.FixedEffectWald => "Common/fixed-effect pooled estimate (Wald)",
            ResearchClaimStatistic.RandomEffectsWald => "Random-effects pooled estimate (Wald)",
            ResearchClaimStatistic.RandomEffectsHksj => "Random-effects pooled estimate (HKSJ)",
            ResearchClaimStatistic.RandomEffectsPredictionInterval => "Random-effects estimate and prediction interval",
            _ => "Deterministic synthesis diagnostic"
        };
        var text = $"{origin}: {number.Label} {number.Operator ?? "="} {Value(number.StudyValue, number.ArtifactValue)}";
        if (number.StudyLower is not null || number.ArtifactLower is not null)
        {
            var interval = claim.Statistic == ResearchClaimStatistic.RandomEffectsPredictionInterval ? "prediction interval" : "confidence interval";
            text += $" ({Decimal(number.ConfidenceLevel!.Value * 100)}% {interval}: {Value(number.StudyLower, number.ArtifactLower)} to {Value(number.StudyUpper, number.ArtifactUpper)})";
        }
        if (number.DegreesOfFreedom is not null) text += $"; df = {number.DegreesOfFreedom.Value}";
        return $"{text}. {scope}";
    }

    public static string SemanticKey(ResearchClaimSemantics claim)
    {
        var canonical = claim with { EvidenceIds = claim.EvidenceIds.Order().ToArray(), Outcome = claim.Outcome?.ToLowerInvariant(), Population = claim.Population?.ToLowerInvariant(),
            ExposureOrIntervention = claim.ExposureOrIntervention?.ToLowerInvariant(), Comparator = claim.Comparator?.ToLowerInvariant(), Timepoint = claim.Timepoint?.ToLowerInvariant() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)))).ToLowerInvariant();
    }

    private static string Known(string? value) => value ?? "not reported";
    private static string Decimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private static string Value(decimal? reported, double? calculated) => reported is not null ? Decimal(reported.Value) :
        calculated is not null && double.IsFinite(calculated.Value) ? calculated.Value.ToString("R", CultureInfo.InvariantCulture) : throw new InvalidOperationException("Missing/non-finite numeric claim value.");
}

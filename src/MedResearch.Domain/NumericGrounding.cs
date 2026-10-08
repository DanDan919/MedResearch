using System.Security.Cryptography;
using System.Text;

namespace MedResearch.Domain;

public enum NumericGroundingStatus
{
    NotApplicable = 0,
    Verified = 1,
    Ambiguous = 2,
    Unsupported = 3
}

public enum NumericGroundingField
{
    SampleSize = 0,
    EffectMeasure = 1,
    EffectEstimate = 2,
    ConfidenceInterval = 3,
    ConfidenceLevel = 4,
    PValue = 5,
    StandardError = 6,
    Outcome = 7,
    Population = 8,
    Comparator = 9,
    Timepoint = 10,
    ExposureOrIntervention = 11
}

public sealed record SourceAnchor(
    Guid SourceMaterialId,
    string NormalizationVersion,
    int StartOffset,
    int EndOffset,
    string SpanHash,
    string Text,
    string? LexicalText = null);

public sealed record NumericGroundingFact(
    NumericGroundingField Field,
    NumericGroundingStatus Status,
    SourceAnchor? Anchor,
    string? Reason);

public static class SourceAnchorIntegrity
{
    public static bool IsValid(SourceAnchor anchor)
    {
        if (anchor.SourceMaterialId == Guid.Empty
            || string.IsNullOrWhiteSpace(anchor.NormalizationVersion)
            || anchor.StartOffset < 0
            || anchor.EndOffset <= anchor.StartOffset
            || anchor.EndOffset - anchor.StartOffset != anchor.Text.Length
            || string.IsNullOrWhiteSpace(anchor.Text)
            || string.IsNullOrWhiteSpace(anchor.SpanHash)
            || (anchor.LexicalText is not null && !string.Equals(
                new string(anchor.LexicalText.Select(char.ToLowerInvariant).ToArray()), anchor.Text, StringComparison.Ordinal)))
        {
            return false;
        }

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(anchor.Text))).ToLowerInvariant();
        return string.Equals(expectedHash, anchor.SpanHash, StringComparison.OrdinalIgnoreCase);
    }
}

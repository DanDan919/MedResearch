using System.Security.Cryptography;
using System.Text;

namespace MedResearch.Application.Research.Extraction;

public sealed record SourceAnchorResolution(
    MedResearch.Domain.NumericGroundingStatus Status,
    MedResearch.Domain.SourceAnchor? Anchor,
    string? Reason);

public sealed class SourceAnchorResolver
{
    public const string NormalizationVersion = "source-text-v1";
    private const int MaxAnchorLength = 1_500;

    public SourceAnchorResolution Resolve(Guid sourceMaterialId, string sourceText, string candidateText)
    {
        if (sourceMaterialId == Guid.Empty)
        {
            return Unsupported("SourceMaterial identity is required.");
        }

        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(candidateText))
        {
            return Unsupported("Source text and candidate anchor text are required.");
        }

        var normalizedSource = Normalize(sourceText);
        var normalizedCandidate = Normalize(candidateText);
        if (normalizedCandidate.Length == 0 || normalizedCandidate.Length > MaxAnchorLength)
        {
            return Unsupported("Candidate anchor is empty or exceeds the bounded anchor length.");
        }

        var first = normalizedSource.IndexOf(normalizedCandidate, StringComparison.Ordinal);
        if (first < 0)
        {
            return Unsupported("Candidate anchor does not occur in the exact SourceMaterial.");
        }

        var second = normalizedSource.IndexOf(normalizedCandidate, first + normalizedCandidate.Length, StringComparison.Ordinal);
        if (second >= 0)
        {
            return new SourceAnchorResolution(
                MedResearch.Domain.NumericGroundingStatus.Ambiguous,
                null,
                "Candidate anchor occurs more than once in the canonical SourceMaterial.");
        }

        var anchor = new MedResearch.Domain.SourceAnchor(
            sourceMaterialId,
            NormalizationVersion,
            first,
            first + normalizedCandidate.Length,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCandidate))).ToLowerInvariant(),
            normalizedCandidate);
        return new SourceAnchorResolution(MedResearch.Domain.NumericGroundingStatus.Verified, anchor, null);
    }

    public static string Normalize(string value)
    {
        return EvidenceGroundingValidator.NormalizeForContainment(value);
    }

    private static SourceAnchorResolution Unsupported(string reason)
    {
        return new SourceAnchorResolution(MedResearch.Domain.NumericGroundingStatus.Unsupported, null, reason);
    }
}

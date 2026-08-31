using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceGroundingValidator
{
    private const int MaxSupportingTextLength = 1_000;

    public bool IsGrounded(string sourceText, string supportingText)
    {
        return TryValidate(sourceText, supportingText, out _);
    }

    public bool TryValidate(string sourceText, string supportingText, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            error = "Source text is required for grounding validation.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(supportingText))
        {
            error = "Supporting text is required.";
            return false;
        }

        if (supportingText.Length > MaxSupportingTextLength)
        {
            error = $"Supporting text exceeds {MaxSupportingTextLength} characters.";
            return false;
        }

        var normalizedSource = NormalizeForContainment(sourceText);
        var normalizedSupportingText = NormalizeForContainment(supportingText);

        if (normalizedSupportingText.Length == 0)
        {
            error = "Supporting text is empty after normalization.";
            return false;
        }

        if (!normalizedSource.Contains(normalizedSupportingText, StringComparison.Ordinal))
        {
            error = "Supporting text does not occur in the supplied source text.";
            return false;
        }

        return true;
    }

    public static string NormalizeForContainment(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}

public sealed class EvidenceNumericGroundingValidator
{
    private static readonly Regex NumericTokenPattern = new(
        @"(?<![A-Za-z0-9.])-?\d+(?:\.\d+)?%?(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool IsGrounded(string sourceText, int value)
    {
        return IsGrounded(sourceText, (decimal)value);
    }

    public bool IsGrounded(string sourceText, decimal value)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        foreach (Match match in NumericTokenPattern.Matches(sourceText))
        {
            var token = match.Value.TrimEnd('%');
            if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                && parsed == value)
            {
                return true;
            }
        }

        return false;
    }
}


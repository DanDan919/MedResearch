using System.Globalization;
using System.Text.RegularExpressions;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Extraction;

public sealed record SemanticNumericGroundingResult(
    IReadOnlyCollection<NumericGroundingFact> Facts,
    string? PValueOperator);

public sealed class SemanticNumericGroundingVerifier
{
    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PValuePattern = new(@"\bp\s*(?<operator>=|<=|>=|<|>|≤|≥)\s*(?<value>-?\d+(?:[.,]\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SampleSizePattern = new(@"(?:\bn\s*=\s*\d+|\b\d+\s+(?:participants?|patients?|subjects?|individuals?|adults?)\b|\b(?:randomi[sz]ed|enrolled|analy[sz]ed|included)\s*[:=]?\s*\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public SemanticNumericGroundingResult Verify(EvidenceFindingDraft finding, SourceAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(anchor);

        var sentences = SplitSentences(anchor.Text);
        var facts = new List<NumericGroundingFact>();
        string? pValueOperator = null;

        AddTextFact(facts, NumericGroundingField.Outcome, finding.Outcome, anchor, sentences, "outcome");
        AddTextFact(facts, NumericGroundingField.Population, finding.Population, anchor, sentences, "population");
        AddTextFact(facts, NumericGroundingField.Comparator, finding.Comparator, anchor, sentences, "comparator");

        if (!string.IsNullOrWhiteSpace(finding.EffectMeasure))
        {
            var effectMeasure = NormalizeMeasure(finding.EffectMeasure);
            var matches = effectMeasure is null
                ? []
                : MatchingSentences(sentences, sentence => ContainsMeasure(sentence, effectMeasure));
            facts.Add(CreateFact(NumericGroundingField.EffectMeasure, matches, anchor, "effect measure"));

            if (finding.EffectValue.HasValue)
            {
                var effectMatches = matches.Where(sentence => ContainsNumber(sentence, finding.EffectValue.Value)).ToArray();
                facts.Add(CreateFact(NumericGroundingField.EffectEstimate, effectMatches, anchor, "effect estimate"));
            }
        }
        else if (finding.EffectValue.HasValue)
        {
            facts.Add(new NumericGroundingFact(NumericGroundingField.EffectEstimate, NumericGroundingStatus.Unsupported, anchor, "Effect estimate has no reported effect measure."));
        }

        if (finding.SampleSize.HasValue)
        {
            var matches = MatchingSentences(sentences, sentence => ContainsNumber(sentence, finding.SampleSize.Value) && SampleSizePattern.IsMatch(sentence));
            matches = matches
                .Where(sentence => !Regex.IsMatch(sentence, @"\b(?:intervention|control|treatment|placebo|arm|subgroup)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
            facts.Add(CreateFact(NumericGroundingField.SampleSize, matches, anchor, "sample size"));
        }

        var hasConfidenceInterval = finding.ConfidenceIntervalLower.HasValue || finding.ConfidenceIntervalUpper.HasValue;
        if (hasConfidenceInterval)
        {
            var matches = MatchingSentences(sentences, sentence =>
                ContainsConfidenceInterval(sentence)
                && finding.ConfidenceIntervalLower.HasValue
                && finding.ConfidenceIntervalUpper.HasValue
                && ContainsNumber(sentence, finding.ConfidenceIntervalLower.Value)
                && ContainsNumber(sentence, finding.ConfidenceIntervalUpper.Value)
                && (!finding.EffectValue.HasValue || ContainsNumber(sentence, finding.EffectValue.Value)));
            facts.Add(CreateFact(NumericGroundingField.ConfidenceInterval, matches, anchor, "confidence interval"));
        }

        if (finding.ConfidenceLevel.HasValue)
        {
            var targetPercent = finding.ConfidenceLevel.Value * 100m;
            var matches = MatchingSentences(sentences, sentence =>
                ContainsConfidenceInterval(sentence)
                && (ContainsNumber(sentence, targetPercent) || ContainsNumber(sentence, finding.ConfidenceLevel.Value)));
            facts.Add(CreateFact(NumericGroundingField.ConfidenceLevel, matches, anchor, "confidence level"));
        }

        if (finding.PValue.HasValue)
        {
            var matches = MatchingSentences(sentences, sentence =>
            {
                var match = PValuePattern.Match(sentence);
                return match.Success
                    && ContainsNumber(match.Groups["value"].Value, finding.PValue.Value)
                    && (!finding.EffectValue.HasValue
                        || (ContainsNumber(sentence, finding.EffectValue.Value)
                            && !string.IsNullOrWhiteSpace(finding.EffectMeasure)
                            && NormalizeMeasure(finding.EffectMeasure) is { } measure
                            && ContainsMeasure(sentence, measure)));
            });
            var pValueFact = CreateFact(NumericGroundingField.PValue, matches, anchor, "p-value");
            if (matches.Count == 1)
            {
                var sourceOperator = NormalizePValueOperator(PValuePattern.Match(matches[0]).Groups["operator"].Value);
                var reportedOperator = NormalizePValueOperator(finding.PValueOperator);
                if (reportedOperator is not null && !string.Equals(reportedOperator, sourceOperator, StringComparison.Ordinal))
                {
                    pValueFact = new NumericGroundingFact(
                        NumericGroundingField.PValue,
                        NumericGroundingStatus.Unsupported,
                        anchor,
                        "Reported p-value operator does not match the source operator.");
                }
                else
                {
                    pValueOperator = sourceOperator;
                }
            }

            facts.Add(pValueFact);
        }

        if (finding.ReportedStandardError.HasValue)
        {
            var matches = MatchingSentences(sentences, sentence =>
                Regex.IsMatch(sentence, @"\b(?:se|standard error)\s*(?:=|:)?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && ContainsNumber(sentence, finding.ReportedStandardError.Value));
            facts.Add(CreateFact(NumericGroundingField.StandardError, matches, anchor, "standard error"));
        }

        return new SemanticNumericGroundingResult(facts, pValueOperator);
    }

    private static void AddTextFact(
        ICollection<NumericGroundingFact> facts,
        NumericGroundingField field,
        string? value,
        SourceAnchor anchor,
        IReadOnlyCollection<string> sentences,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var matches = MatchingSentences(sentences, sentence => sentence.Contains(SourceAnchorResolver.Normalize(value), StringComparison.Ordinal));
        facts.Add(CreateFact(field, matches, anchor, label));
    }

    private static NumericGroundingFact CreateFact(
        NumericGroundingField field,
        IReadOnlyCollection<string> matches,
        SourceAnchor anchor,
        string label)
    {
        return matches.Count switch
        {
            1 => new NumericGroundingFact(field, NumericGroundingStatus.Verified, anchor, null),
            > 1 => new NumericGroundingFact(field, NumericGroundingStatus.Ambiguous, anchor, $"More than one local {label} context matched."),
            _ => new NumericGroundingFact(field, NumericGroundingStatus.Unsupported, anchor, $"No local {label} context matched the extracted value.")
        };
    }

    private static IReadOnlyList<string> MatchingSentences(
        IReadOnlyCollection<string> sentences,
        Func<string, bool> predicate)
    {
        return sentences.Where(predicate).ToArray();
    }

    private static IReadOnlyList<string> SplitSentences(string text)
    {
        var sentences = SentenceBoundary.Split(text)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .ToArray();
        return sentences.Length == 0 ? [text] : sentences;
    }

    private static bool ContainsConfidenceInterval(string sentence)
    {
        return sentence.Contains("confidence interval", StringComparison.Ordinal)
            || Regex.IsMatch(sentence, @"\bci\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsMeasure(string sentence, string measure)
    {
        return measure switch
        {
            "or" => Regex.IsMatch(sentence, @"\b(?:or|odds ratio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "rr" => Regex.IsMatch(sentence, @"\b(?:rr|risk ratio|relative risk)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "hr" => Regex.IsMatch(sentence, @"\b(?:hr|hazard ratio)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "md" => Regex.IsMatch(sentence, @"\b(?:md|mean difference)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "smd" => Regex.IsMatch(sentence, @"\b(?:smd|standardized mean difference|standardised mean difference|cohen d|hedges g)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "rd" => Regex.IsMatch(sentence, @"\b(?:rd|risk difference|absolute risk difference)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "correlation" => Regex.IsMatch(sentence, @"\b(?:r|correlation|pearson r|spearman r)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => sentence.Contains(measure, StringComparison.Ordinal)
        };
    }

    private static string? NormalizeMeasure(string value)
    {
        var normalized = SourceAnchorResolver.Normalize(value);
        return normalized switch
        {
            "or" or "odds ratio" => "or",
            "rr" or "risk ratio" or "relative risk" => "rr",
            "hr" or "hazard ratio" => "hr",
            "md" or "mean difference" => "md",
            "smd" or "standardized mean difference" or "standardised mean difference" or "cohen d" or "hedges g" => "smd",
            "rd" or "risk difference" or "absolute risk difference" => "rd",
            "r" or "correlation" or "pearson r" or "spearman r" => "correlation",
            _ => null
        };
    }

    private static bool ContainsNumber(string text, decimal value)
    {
        var target = value.ToString(CultureInfo.InvariantCulture);
        return ContainsNumber(text, target, value);
    }

    private static bool ContainsNumber(string text, string candidate, decimal value)
    {
        var normalizedCandidate = candidate.Replace(',', '.');
        if (decimal.TryParse(normalizedCandidate, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedCandidate)
            && parsedCandidate == value
            && Regex.IsMatch(text, $@"(?<![A-Za-z0-9.]){Regex.Escape(normalizedCandidate)}(?![A-Za-z0-9.])", RegexOptions.CultureInvariant))
        {
            return true;
        }

        var pattern = @"(?<![A-Za-z0-9.])-?\d+(?:[.,]\d+)?";
        return Regex.Matches(text, pattern, RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Any(match => decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed == value);
    }

    private static bool ContainsNumber(string text, int value)
    {
        return ContainsNumber(text, (decimal)value);
    }

    private static string? NormalizePValueOperator(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            "=" => "=",
            "<" => "<",
            ">" => ">",
            "<=" or "≤" => "<=",
            ">=" or "≥" => ">=",
            _ => null
        };
    }
}

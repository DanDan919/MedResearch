using System.Globalization;
using System.Text.RegularExpressions;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Extraction;

public sealed record SemanticNumericGroundingResult(IReadOnlyCollection<NumericGroundingFact> Facts, string? PValueOperator);

public sealed class SemanticNumericGroundingVerifier
{
    public const string AlgorithmVersion = "statistical-tuple-v1";
    private const string Number = @"[+\-−–]?\d+(?:[.,]\d+)?";
    private const RegexOptions Flags = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly Regex Clauses = new(@"(?<=[.!?;])\s+|\s*,?\s*\b(?:whereas|while|but)\b\s+", Flags);
    private static readonly Regex Effect = new($@"\b(?<measure>odds ratio|relative risk|risk ratio|hazard ratio|standardized mean difference|standardised mean difference|mean difference|absolute risk difference|risk difference|pearson r|spearman r|correlation|cohen d|hedges g|smd|md|or|rr|hr|rd|r)\s*(?:was\s+|of\s+|=\s*|:\s*)?(?<value>{Number})(?!\d|[.,]\d)", Flags);
    private static readonly Regex Interval = new($@"(?:(?<level>\d+(?:\.\d+)?)\s*%\s*)?\b(?:ci|confidence interval)\s*[:=]?\s*[\[(]?\s*(?<low>{Number})\s*(?:to|and|[–−\-,])\s*(?<high>{Number})(?!\d|[.,]\d)", Flags);
    private static readonly Regex StandardError = new($@"\b(?:se|standard error)\s*[:=]?\s*(?<value>{Number})(?!\d|[.,]\d)", Flags);
    private static readonly Regex PValue = new($@"\bp\s*(?<operator><=|>=|=|<|>|≤|≥)\s*(?<value>{Number})(?!\d|[.,]\d)", Flags);
    private static readonly Regex Participants = new(@"\b(?<value>\d+)\s+(?:participants?|patients?|subjects?|individuals?|adults?)\b", Flags);
    private static readonly Regex ExplicitN = new(@"\bn\s*=\s*(?<value>\d+)\b", Flags);
    private static readonly Regex ParticipantNPrefix = new(@"\b(?:participants?|patients?|subjects?|individuals?|adults?)\s*(?:(?:were\s+)?(?:randomi[sz]ed|enrolled|analy[sz]ed)\s*)?[(:,]*\s*$", Flags);
    private static readonly Regex OtherCountUnit = new(@"\G\s+(?:hospitals?|clinics?|centres?|centers?|sites?|clusters?|wards?|visits?|events?|observations?|trials?|studies)\b", Flags);
    private static readonly Regex LimitedScope = new(@"\b(?:intervention|control|treatment|placebo|arm|subgroup)\b", Flags);
    private static readonly Regex StatisticalSeparator = new(@"\G[\s(),:\[\]]*(?:(?:with(?:\s+a)?|and)\s+)?", Flags);

    public SemanticNumericGroundingResult Verify(EvidenceFindingDraft finding, SourceAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(anchor);
        var facts = new List<NumericGroundingFact>();
        var contexts = Clauses.Split(anchor.LexicalText ?? anchor.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        // Canonical containment is case-insensitive; bare OR needs source-case
        // proof to distinguish the abbreviation from the English conjunction.
        var expressions = contexts.Select(text => new ResultContext(text, Effect.Matches(text).Cast<Match>()
            .Where(match => !string.Equals(match.Groups["measure"].Value, "or", StringComparison.OrdinalIgnoreCase)
                || (anchor.LexicalText is not null && match.Groups["measure"].Value == "OR")).ToArray())).ToArray();
        var selected = expressions.Where(context => context.Effects.Length == 1
            && NormalizeMeasure(context.Effects[0].Groups["measure"].Value) == NormalizeMeasure(finding.EffectMeasure)
            && finding.EffectValue.HasValue && Equal(context.Effects[0].Groups["value"].Value, finding.EffectValue.Value)
            && BindsOutcome(context.Text[..context.Effects[0].Index], finding.Outcome)).ToArray();
        var multiple = selected.Length > 1 || expressions.Any(context => context.Effects.Length > 1
            && ContainsPhrase(context.Text, finding.Outcome));
        var tuple = !multiple && selected.Length == 1 ? selected[0] : null;
        var status = multiple ? NumericGroundingStatus.Ambiguous : NumericGroundingStatus.Unsupported;

        void Add(NumericGroundingField field, bool supplied, bool supported, bool ambiguous = false)
        {
            if (supplied)
            {
                var result = ambiguous ? NumericGroundingStatus.Ambiguous : supported ? NumericGroundingStatus.Verified : status;
                facts.Add(new(field, result, anchor, result == NumericGroundingStatus.Verified ? null : $"{AlgorithmVersion}: no unique bound {field} role/context."));
            }
        }

        // Estimand labels must precede the selected expression, not occur in a
        // different sentence or in free text following another result.
        var contextText = tuple is null ? null : tuple.Text[..(tuple.Effects[0].Index + tuple.Effects[0].Groups["measure"].Length)];
        Add(NumericGroundingField.Outcome, !string.IsNullOrWhiteSpace(finding.Outcome), tuple is not null);
        var overallPopulation = tuple is not null && expressions.Sum(context => context.Effects.Length) == 1
            && contexts.Count(text => Participants.IsMatch(text) && !LimitedScope.IsMatch(text)
                && Regex.IsMatch(text, @"\b(?:overall|total|randomi[sz]ed|enrolled|analy[sz]ed)\b", Flags)) == 1
            && contexts.Any(text => Participants.IsMatch(text) && !LimitedScope.IsMatch(text)
                && Regex.IsMatch(text, @"\b(?:overall|total|randomi[sz]ed|enrolled|analy[sz]ed)\b", Flags) && ContainsPhrase(text, finding.Population));
        Add(NumericGroundingField.Population, !string.IsNullOrWhiteSpace(finding.Population), ContainsPhrase(contextText, finding.Population) || overallPopulation);
        var comparisonBound = !string.IsNullOrWhiteSpace(contextText) && !string.IsNullOrWhiteSpace(finding.ExposureOrIntervention) && !string.IsNullOrWhiteSpace(finding.Comparator)
            && Regex.IsMatch(contextText, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(SourceAnchorResolver.Normalize(finding.ExposureOrIntervention))}\s+(?:versus|vs\.?|compared (?:with|to)|against)\s+{Regex.Escape(SourceAnchorResolver.Normalize(finding.Comparator))}(?![\p{{L}}\p{{N}}])", Flags);
        Add(NumericGroundingField.ExposureOrIntervention, !string.IsNullOrWhiteSpace(finding.ExposureOrIntervention), comparisonBound);
        Add(NumericGroundingField.Comparator, !string.IsNullOrWhiteSpace(finding.Comparator), comparisonBound);
        var timepointBound = ContainsPhrase(contextText, finding.Timepoint) && Regex.IsMatch(contextText!,
            $@"(?:\b(?:at|after)\s+{Regex.Escape(SourceAnchorResolver.Normalize(finding.Timepoint!))}(?![\p{{L}}\p{{N}}])|(?<![\p{{L}}\p{{N}}]){Regex.Escape(SourceAnchorResolver.Normalize(finding.Timepoint!))}\s+follow[- ]up\b)", Flags);
        Add(NumericGroundingField.Timepoint, !string.IsNullOrWhiteSpace(finding.Timepoint), timepointBound);
        Add(NumericGroundingField.EffectMeasure, !string.IsNullOrWhiteSpace(finding.EffectMeasure), tuple is not null);
        Add(NumericGroundingField.EffectEstimate, finding.EffectValue.HasValue, tuple is not null);

        var statistics = tuple is null ? [] : BoundStatistics(tuple);
        var intervals = statistics.Where(statistic => statistic.Role == NumericGroundingField.ConfidenceInterval).Select(statistic => statistic.Match).ToArray();
        var boundInterval = intervals.Length == 1 && finding.ConfidenceIntervalLower.HasValue && finding.ConfidenceIntervalUpper.HasValue
            && Equal(intervals[0].Groups["low"].Value, finding.ConfidenceIntervalLower.Value)
            && Equal(intervals[0].Groups["high"].Value, finding.ConfidenceIntervalUpper.Value);
        Add(NumericGroundingField.ConfidenceInterval, finding.ConfidenceIntervalLower.HasValue || finding.ConfidenceIntervalUpper.HasValue, boundInterval, intervals.Length > 1);
        Add(NumericGroundingField.ConfidenceLevel, finding.ConfidenceLevel.HasValue,
            boundInterval && Equal(intervals[0].Groups["level"].Value, finding.ConfidenceLevel.GetValueOrDefault() * 100m), intervals.Length > 1);

        var errors = statistics.Where(statistic => statistic.Role == NumericGroundingField.StandardError).Select(statistic => statistic.Match).ToArray();
        Add(NumericGroundingField.StandardError, finding.ReportedStandardError.HasValue,
            errors.Length == 1 && Equal(errors[0].Groups["value"].Value, finding.ReportedStandardError.GetValueOrDefault()), errors.Length > 1);
        var pValues = statistics.Where(statistic => statistic.Role == NumericGroundingField.PValue).Select(statistic => statistic.Match).ToArray();
        var sourceOperator = pValues.Length == 1 ? NormalizeOperator(pValues[0].Groups["operator"].Value) : null;
        var boundP = pValues.Length == 1 && Equal(pValues[0].Groups["value"].Value, finding.PValue.GetValueOrDefault())
            && (string.IsNullOrWhiteSpace(finding.PValueOperator) || NormalizeOperator(finding.PValueOperator) == sourceOperator);
        Add(NumericGroundingField.PValue, finding.PValue.HasValue, boundP, pValues.Length > 1);

        if (finding.SampleSize.HasValue)
        {
            var scoped = contexts.Where(text => !LimitedScope.IsMatch(text)).ToArray();
            var scopedCounts = scoped.Where(text => Regex.IsMatch(text, @"\b(?:overall|total|randomi[sz]ed|enrolled|analy[sz]ed)\b", Flags))
                .SelectMany(text => Participants.Matches(text).Cast<Match>()
                    .Concat(ExplicitN.Matches(text).Cast<Match>()
                        .Where(match => ParticipantNPrefix.IsMatch(text[..match.Index])
                            && !OtherCountUnit.IsMatch(text, match.Index + match.Length)))
                    .DistinctBy(match => match.Groups["value"].Index))
                .ToArray();
            var matches = scopedCounts.Where(match => Equal(match.Groups["value"].Value, finding.SampleSize.Value)).ToArray();
            var unknownScope = matches.Length == 0 && scoped.Any(text => Participants.Matches(text).Cast<Match>()
                .Concat(ExplicitN.Matches(text).Cast<Match>()).Any(match => Equal(match.Groups["value"].Value, finding.SampleSize.Value)));
            var sampleAmbiguous = matches.Length > 0 && (scopedCounts.Length != 1 || expressions.Sum(context => context.Effects.Length) > 1);
            Add(NumericGroundingField.SampleSize, true, matches.Length == 1, sampleAmbiguous || unknownScope);
        }
        return new(facts, finding.PValue.HasValue && boundP ? sourceOperator : null);
    }

    private static IReadOnlyList<(NumericGroundingField Role, Match Match)> BoundStatistics(ResultContext context)
    {
        var result = new List<(NumericGroundingField, Match)>();
        var offset = context.Effects[0].Index + context.Effects[0].Length;
        while (offset < context.Text.Length)
        {
            var separator = StatisticalSeparator.Match(context.Text, offset);
            var start = offset + separator.Length;
            var found = false;
            foreach (var (role, pattern) in new[] { (NumericGroundingField.ConfidenceInterval, Interval), (NumericGroundingField.StandardError, StandardError), (NumericGroundingField.PValue, PValue) })
            {
                var match = pattern.Match(context.Text, start);
                if (match.Success && match.Index == start)
                {
                    result.Add((role, match));
                    offset = match.Index + match.Length;
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }
        return result;
    }

    private static bool ContainsPhrase(string? text, string? value) => !string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(value)
        && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(SourceAnchorResolver.Normalize(value))}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool BindsOutcome(string prefix, string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) return false;
        // Only explicit outcome -> reporting connector -> measure syntax qualifies.
        // Merely mentioning another outcome earlier in the clause is not proof.
        var match = Regex.Match(prefix, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(SourceAnchorResolver.Normalize(outcome))}(?![\p{{L}}\p{{N}}])[\s,:]*(?:(?:the|was|were|showed|had|reported|improved|reduced|increased|decreased|lower|higher|with)\b[\s,:]*)*$", Flags);
        return match.Success && !Regex.IsMatch(prefix[..match.Index], @"\b(?:and|or)\s*$", Flags);
    }

    private static bool Equal(string text, decimal value) => decimal.TryParse(text.Replace('−', '-').Replace('–', '-').Replace(',', '.'),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed == value;

    private static string? NormalizeMeasure(string? value) => value is null ? null : SourceAnchorResolver.Normalize(value) switch
    {
        "or" or "odds ratio" => "or", "rr" or "risk ratio" or "relative risk" => "rr", "hr" or "hazard ratio" => "hr",
        "md" or "mean difference" => "md", "smd" or "standardized mean difference" or "standardised mean difference" or "cohen d" or "hedges g" => "smd",
        "rd" or "risk difference" or "absolute risk difference" => "rd", "r" or "correlation" or "pearson r" or "spearman r" => "correlation", _ => null
    };
    private static string? NormalizeOperator(string? value) => value?.Trim() switch { "≤" => "<=", "≥" => ">=", "=" or "<" or ">" or "<=" or ">=" => value.Trim(), _ => null };
    private sealed record ResultContext(string Text, Match[] Effects);
}

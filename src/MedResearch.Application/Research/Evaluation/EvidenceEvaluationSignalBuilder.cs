using MedResearch.Domain;

namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationSignalBuilder
{
    public EvidenceEvaluationSignalSet Build(EvaluationStudyContext context)
    {
        var evidence = context.Evidence;
        var reportingLimitations = new List<string>();

        if (context.SourceScope == EvidenceSourceScope.Abstract)
        {
            reportingLimitations.Add("Current source scope is abstract-level only; detailed methods and risk-of-bias domains may be insufficiently reported.");
        }

        if (evidence.Count == 0)
        {
            reportingLimitations.Add("No extracted source-grounded evidence findings are available for this study in the current run.");
        }

        var hasSampleSize = evidence.Any(item => item.SampleSize.HasValue);
        var hasEffectEstimate = evidence.Any(item => item.EffectValue.HasValue);
        var hasConfidenceInterval = evidence.Any(item => item.ConfidenceIntervalLower.HasValue || item.ConfidenceIntervalUpper.HasValue);
        var hasPValue = evidence.Any(item => item.PValue.HasValue);
        var hasComparator = evidence.Any(item => !string.IsNullOrWhiteSpace(item.Comparator))
            || !string.IsNullOrWhiteSpace(context.Plan?.Comparator);

        if (!hasConfidenceInterval)
        {
            reportingLimitations.Add("Confidence interval is unavailable in the validated extracted evidence.");
        }

        if (!hasEffectEstimate)
        {
            reportingLimitations.Add("Effect estimate is unavailable in the validated extracted evidence.");
        }

        if (!hasSampleSize)
        {
            reportingLimitations.Add("Sample size is unavailable in the validated extracted evidence.");
        }

        return new EvidenceEvaluationSignalSet(
            context.SourceScope,
            evidence.Count,
            hasSampleSize,
            hasEffectEstimate,
            hasConfidenceInterval,
            hasPValue,
            hasComparator,
            InferStudyDesignHint(context.PublicationTypes, context.Title, context.Abstract),
            reportingLimitations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static StudyDesignClassification InferStudyDesignHint(
        IReadOnlyCollection<string> publicationTypes,
        string title,
        string? abstractText)
    {
        var haystack = string.Join(' ', publicationTypes.Append(title).Append(abstractText ?? string.Empty)).ToLowerInvariant();

        if (haystack.Contains("meta-analysis", StringComparison.Ordinal) || haystack.Contains("meta analysis", StringComparison.Ordinal))
        {
            return StudyDesignClassification.MetaAnalysis;
        }

        if (haystack.Contains("systematic review", StringComparison.Ordinal))
        {
            return StudyDesignClassification.SystematicReview;
        }

        if (haystack.Contains("randomized", StringComparison.Ordinal) || haystack.Contains("randomised", StringComparison.Ordinal))
        {
            return StudyDesignClassification.RandomizedControlledTrial;
        }

        if (haystack.Contains("cohort", StringComparison.Ordinal))
        {
            return StudyDesignClassification.Cohort;
        }

        if (haystack.Contains("case-control", StringComparison.Ordinal) || haystack.Contains("case control", StringComparison.Ordinal))
        {
            return StudyDesignClassification.CaseControl;
        }

        if (haystack.Contains("cross-sectional", StringComparison.Ordinal) || haystack.Contains("cross sectional", StringComparison.Ordinal))
        {
            return StudyDesignClassification.CrossSectional;
        }

        if (haystack.Contains("case report", StringComparison.Ordinal))
        {
            return StudyDesignClassification.CaseReport;
        }

        return StudyDesignClassification.Unknown;
    }
}

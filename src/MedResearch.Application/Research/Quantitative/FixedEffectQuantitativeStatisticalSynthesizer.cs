namespace MedResearch.Application.Research.Quantitative;

public sealed class FixedEffectQuantitativeStatisticalSynthesizer : IQuantitativeStatisticalSynthesizer
{
    public const string AlgorithmVersion = "fixed-effect-inverse-variance-v1";

    private readonly QuantitativeSynthesisOptions _options;

    public FixedEffectQuantitativeStatisticalSynthesizer(QuantitativeSynthesisOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public QuantitativeSynthesisReadiness Synthesize(QuantitativeEvidenceReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var assessmentsByEvidenceId = readiness.Assessments.ToDictionary(assessment => assessment.EvidenceId);
        var results = readiness.CompatibleGroups
            .OrderBy(group => group.GroupKey, StringComparer.Ordinal)
            .Select(group => SynthesizeGroup(readiness.ResearchRunId, group, assessmentsByEvidenceId))
            .ToArray();

        return new QuantitativeSynthesisReadiness(
            readiness.ResearchRunId,
            results,
            results.Count(result => result.Status == QuantitativeSynthesisStatus.Synthesized),
            results.Count(result => result.Status == QuantitativeSynthesisStatus.NotSynthesizable),
            AlgorithmVersion);
    }

    private QuantitativeSynthesisResult SynthesizeGroup(
        Guid researchRunId,
        CompatibleEvidenceGroup group,
        IReadOnlyDictionary<Guid, QuantitativeEvidenceAssessment> assessmentsByEvidenceId)
    {
        var reasons = ValidateGroup(group, _options.MinimumUniqueStudies).ToList();
        var orderedAssessments = group.EvidenceIds
            .Select(evidenceId => assessmentsByEvidenceId.TryGetValue(evidenceId, out var assessment) ? assessment : null)
            .Where(assessment => assessment is not null)
            .Select(assessment => assessment!)
            .OrderBy(assessment => assessment.StudyId)
            .ThenBy(assessment => assessment.EvidenceId)
            .ToArray();

        if (orderedAssessments.Length != group.EvidenceIds.Distinct().Count())
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.MissingEligibleEvidence);
        }

        if (orderedAssessments.Select(assessment => assessment.EvidenceId).Distinct().Count() != orderedAssessments.Length)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.DuplicateEvidenceContribution);
        }

        var preflight = BuildPreflightContributions(orderedAssessments, reasons);
        if (reasons.Count > 0)
        {
            return CreateRejected(researchRunId, group, preflight, reasons);
        }

        var totalWeight = preflight.Sum(contribution => contribution.Weight);
        if (!double.IsFinite(totalWeight) || totalWeight <= 0)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.InvalidWeight);
            return CreateRejected(researchRunId, group, preflight, reasons);
        }

        var contributions = preflight
            .Select(contribution => contribution with { NormalizedWeight = contribution.Weight / totalWeight })
            .ToArray();
        if (contributions.Any(contribution => !double.IsFinite(contribution.NormalizedWeight) || contribution.NormalizedWeight <= 0))
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.InvalidWeight);
            return CreateRejected(researchRunId, group, contributions, reasons);
        }

        var pooledEffect = contributions.Sum(contribution => contribution.Weight * contribution.AnalysisScaleEffect) / totalWeight;
        var pooledVariance = 1d / totalWeight;
        var pooledStandardError = Math.Sqrt(pooledVariance);
        if (!double.IsFinite(pooledEffect) || !double.IsFinite(pooledVariance) || !double.IsFinite(pooledStandardError)
            || pooledVariance <= 0 || pooledStandardError <= 0)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.NonFinitePooledEffect);
            return CreateRejected(researchRunId, group, contributions, reasons);
        }

        var z = StandardNormalQuantile.Inverse(0.5d + _options.BoundedOutputConfidenceLevel / 2d);
        var lower = pooledEffect - z * pooledStandardError;
        var upper = pooledEffect + z * pooledStandardError;
        if (!double.IsFinite(z) || z <= 0 || !double.IsFinite(lower) || !double.IsFinite(upper) || lower > upper)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.NonFiniteConfidenceInterval);
            return CreateRejected(researchRunId, group, contributions, reasons);
        }

        var reportedEffect = Math.Exp(pooledEffect);
        var reportedLower = Math.Exp(lower);
        var reportedUpper = Math.Exp(upper);
        if (!double.IsFinite(reportedEffect) || !double.IsFinite(reportedLower) || !double.IsFinite(reportedUpper)
            || reportedEffect <= 0 || reportedLower <= 0 || reportedUpper <= 0)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.BackTransformationFailed);
            return CreateRejected(researchRunId, group, contributions, reasons);
        }

        return new QuantitativeSynthesisResult(
            researchRunId,
            group.GroupKey,
            group.OutcomeGroupKey,
            group.PopulationCompatibilityKey,
            group.ComparatorCompatibilityKey,
            group.StudyDesignCompatibilityKey,
            group.EffectMeasureType,
            QuantitativeSynthesisStatus.Synthesized,
            QuantitativeSynthesisMethod.FixedEffectInverseVariance,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            group.EvidenceCount,
            group.UniqueStudyCount,
            pooledEffect,
            pooledVariance,
            pooledStandardError,
            lower,
            upper,
            reportedEffect,
            reportedLower,
            reportedUpper,
            contributions,
            []);
    }

    private static IReadOnlyCollection<QuantitativeSynthesisRejectionReason> ValidateGroup(CompatibleEvidenceGroup group, int minimumUniqueStudies)
    {
        var reasons = new List<QuantitativeSynthesisRejectionReason>();

        if (!group.ReadyForFutureMetaAnalysisInput)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.GroupNotReadyForMetaAnalysisInput);
        }

        if (group.UniqueStudyCount < minimumUniqueStudies)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.InsufficientIndependentStudies);
        }

        if (group.HasDependentEvidenceFromSameStudy)
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.DependentEvidenceFromSameStudy);
        }

        if (!IsSupportedRatioMeasure(group.EffectMeasureType))
        {
            reasons.Add(QuantitativeSynthesisRejectionReason.UnsupportedEffectMeasure);
        }

        return reasons;
    }

    private static IReadOnlyCollection<QuantitativeSynthesisContribution> BuildPreflightContributions(
        IReadOnlyCollection<QuantitativeEvidenceAssessment> assessments,
        ICollection<QuantitativeSynthesisRejectionReason> reasons)
    {
        var contributions = new List<QuantitativeSynthesisContribution>();

        foreach (var assessment in assessments)
        {
            if (assessment.Eligibility != QuantitativeEligibility.Eligible)
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.MissingEligibleEvidence);
                continue;
            }

            if (!assessment.NormalizedEffect.HasValue || !double.IsFinite(assessment.NormalizedEffect.Value))
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.MissingNormalizedEffect);
                continue;
            }

            if (!assessment.Variance.HasValue)
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.MissingVariance);
                continue;
            }

            var variance = assessment.Variance.Value;
            if (!double.IsFinite(variance) || variance <= 0)
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.InvalidVariance);
                continue;
            }

            var standardError = assessment.StandardError ?? Math.Sqrt(variance);
            if (!double.IsFinite(standardError) || standardError <= 0)
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.InvalidVariance);
                continue;
            }

            var weight = 1d / variance;
            if (!double.IsFinite(weight) || weight <= 0)
            {
                reasons.Add(QuantitativeSynthesisRejectionReason.InvalidWeight);
                continue;
            }

            contributions.Add(new QuantitativeSynthesisContribution(
                assessment.EvidenceId,
                assessment.StudyId,
                assessment.EvidenceExtractionId,
                assessment.SourceMaterialId,
                assessment.NormalizedEffect.Value,
                variance,
                standardError,
                weight,
                0d));
        }

        return contributions;
    }

    private QuantitativeSynthesisResult CreateRejected(
        Guid researchRunId,
        CompatibleEvidenceGroup group,
        IReadOnlyCollection<QuantitativeSynthesisContribution> contributions,
        IReadOnlyCollection<QuantitativeSynthesisRejectionReason> reasons)
    {
        return new QuantitativeSynthesisResult(
            researchRunId,
            group.GroupKey,
            group.OutcomeGroupKey,
            group.PopulationCompatibilityKey,
            group.ComparatorCompatibilityKey,
            group.StudyDesignCompatibilityKey,
            group.EffectMeasureType,
            QuantitativeSynthesisStatus.NotSynthesizable,
            QuantitativeSynthesisMethod.FixedEffectInverseVariance,
            AlgorithmVersion,
            _options.OutputConfidenceLevel,
            group.EvidenceCount,
            group.UniqueStudyCount,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            contributions,
            reasons.Distinct().OrderBy(reason => reason).ToArray());
    }

    private static bool IsSupportedRatioMeasure(EffectMeasureType effectMeasureType)
    {
        return effectMeasureType is EffectMeasureType.OddsRatio
            or EffectMeasureType.RiskRatio
            or EffectMeasureType.HazardRatio;
    }
}

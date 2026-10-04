using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MedResearch.Application.Research.Quantitative;

public sealed record QuantitativeSynthesisArtifactReadModel(
    Guid ArtifactId,
    DateTimeOffset PersistedAt,
    string SnapshotFingerprint,
    QuantitativeSynthesisResult Result);

public interface IQuantitativeSynthesisArtifactStore
{
    Task PersistAsync(QuantitativeSynthesisReadiness readiness, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> FindByResearchRunIdAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken);
}

public static class QuantitativeSynthesisArtifactSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string ComputeFingerprint(QuantitativeSynthesisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateFinite(result);

        var canonical = result with
        {
            Contributions = result.Contributions
                .OrderBy(contribution => contribution.StudyId)
                .ThenBy(contribution => contribution.EvidenceId)
                .ToArray(),
            RejectionReasons = result.RejectionReasons.OrderBy(reason => reason).ToArray(),
            RandomEffects = result.RandomEffects is null
                ? null
                : result.RandomEffects with
                {
                    Contributions = result.RandomEffects.Contributions
                        .OrderBy(contribution => contribution.StudyId)
                        .ThenBy(contribution => contribution.EvidenceId)
                        .ToArray(),
                    FailureReasons = result.RandomEffects.FailureReasons.OrderBy(reason => reason).ToArray(),
                    HksjInference = result.RandomEffects.HksjInference is null
                        ? null
                        : result.RandomEffects.HksjInference with
                        {
                            FailureReasons = result.RandomEffects.HksjInference.FailureReasons.OrderBy(reason => reason).ToArray()
                        },
                    PredictionInterval = result.RandomEffects.PredictionInterval is null
                        ? null
                        : result.RandomEffects.PredictionInterval with
                        {
                            FailureReasons = result.RandomEffects.PredictionInterval.FailureReasons.OrderBy(reason => reason).ToArray()
                        }
                }
        };

        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static void ValidateFinite(QuantitativeSynthesisResult result)
    {
        ValidateFinite(result.AnalysisScaleEffect, nameof(result.AnalysisScaleEffect));
        ValidateFinite(result.AnalysisScaleVariance, nameof(result.AnalysisScaleVariance));
        ValidateFinite(result.AnalysisScaleStandardError, nameof(result.AnalysisScaleStandardError));
        ValidateFinite(result.AnalysisScaleConfidenceIntervalLower, nameof(result.AnalysisScaleConfidenceIntervalLower));
        ValidateFinite(result.AnalysisScaleConfidenceIntervalUpper, nameof(result.AnalysisScaleConfidenceIntervalUpper));
        ValidateFinite(result.ReportedScaleEffect, nameof(result.ReportedScaleEffect));
        ValidateFinite(result.ReportedScaleConfidenceIntervalLower, nameof(result.ReportedScaleConfidenceIntervalLower));
        ValidateFinite(result.ReportedScaleConfidenceIntervalUpper, nameof(result.ReportedScaleConfidenceIntervalUpper));

        if (result.HeterogeneityDiagnostics is not null)
        {
            ValidateFinite(result.HeterogeneityDiagnostics.CochransQ, nameof(result.HeterogeneityDiagnostics.CochransQ));
            ValidateFinite(result.HeterogeneityDiagnostics.ISquared, nameof(result.HeterogeneityDiagnostics.ISquared));
        }

        if (result.BetweenStudyVariance is not null)
        {
            ValidateFinite(result.BetweenStudyVariance.TauSquared, nameof(result.BetweenStudyVariance.TauSquared));
        }

        if (result.RandomEffects is not null)
        {
            ValidateFinite(result.RandomEffects.TauSquared, nameof(result.RandomEffects.TauSquared));
            ValidateFinite(result.RandomEffects.AnalysisScaleEffect, nameof(result.RandomEffects.AnalysisScaleEffect));
            ValidateFinite(result.RandomEffects.AnalysisScaleVariance, nameof(result.RandomEffects.AnalysisScaleVariance));
            ValidateFinite(result.RandomEffects.AnalysisScaleStandardError, nameof(result.RandomEffects.AnalysisScaleStandardError));
            ValidateFinite(result.RandomEffects.AnalysisScaleConfidenceIntervalLower, nameof(result.RandomEffects.AnalysisScaleConfidenceIntervalLower));
            ValidateFinite(result.RandomEffects.AnalysisScaleConfidenceIntervalUpper, nameof(result.RandomEffects.AnalysisScaleConfidenceIntervalUpper));
            ValidateFinite(result.RandomEffects.ReportedScaleEffect, nameof(result.RandomEffects.ReportedScaleEffect));
            ValidateFinite(result.RandomEffects.ReportedScaleConfidenceIntervalLower, nameof(result.RandomEffects.ReportedScaleConfidenceIntervalLower));
            ValidateFinite(result.RandomEffects.ReportedScaleConfidenceIntervalUpper, nameof(result.RandomEffects.ReportedScaleConfidenceIntervalUpper));
            ValidateContributions(result.RandomEffects.Contributions);

            if (result.RandomEffects.HksjInference is not null)
            {
                ValidateFinite(result.RandomEffects.HksjInference.VarianceAdjustment, nameof(result.RandomEffects.HksjInference.VarianceAdjustment));
                ValidateFinite(result.RandomEffects.HksjInference.CriticalValue, nameof(result.RandomEffects.HksjInference.CriticalValue));
                ValidateFinite(result.RandomEffects.HksjInference.AnalysisScaleEffect, nameof(result.RandomEffects.HksjInference.AnalysisScaleEffect));
                ValidateFinite(result.RandomEffects.HksjInference.AnalysisScaleVariance, nameof(result.RandomEffects.HksjInference.AnalysisScaleVariance));
                ValidateFinite(result.RandomEffects.HksjInference.AnalysisScaleStandardError, nameof(result.RandomEffects.HksjInference.AnalysisScaleStandardError));
                ValidateFinite(result.RandomEffects.HksjInference.AnalysisScaleConfidenceIntervalLower, nameof(result.RandomEffects.HksjInference.AnalysisScaleConfidenceIntervalLower));
                ValidateFinite(result.RandomEffects.HksjInference.AnalysisScaleConfidenceIntervalUpper, nameof(result.RandomEffects.HksjInference.AnalysisScaleConfidenceIntervalUpper));
                ValidateFinite(result.RandomEffects.HksjInference.ReportedScaleEffect, nameof(result.RandomEffects.HksjInference.ReportedScaleEffect));
                ValidateFinite(result.RandomEffects.HksjInference.ReportedScaleConfidenceIntervalLower, nameof(result.RandomEffects.HksjInference.ReportedScaleConfidenceIntervalLower));
                ValidateFinite(result.RandomEffects.HksjInference.ReportedScaleConfidenceIntervalUpper, nameof(result.RandomEffects.HksjInference.ReportedScaleConfidenceIntervalUpper));
            }

            if (result.RandomEffects.PredictionInterval is not null)
            {
                ValidateFinite(result.RandomEffects.PredictionInterval.TauSquared, nameof(result.RandomEffects.PredictionInterval.TauSquared));
                ValidateFinite(result.RandomEffects.PredictionInterval.SummaryEffectVariance, nameof(result.RandomEffects.PredictionInterval.SummaryEffectVariance));
                ValidateFinite(result.RandomEffects.PredictionInterval.SummaryEffectStandardError, nameof(result.RandomEffects.PredictionInterval.SummaryEffectStandardError));
                ValidateFinite(result.RandomEffects.PredictionInterval.PredictionVariance, nameof(result.RandomEffects.PredictionInterval.PredictionVariance));
                ValidateFinite(result.RandomEffects.PredictionInterval.PredictionStandardError, nameof(result.RandomEffects.PredictionInterval.PredictionStandardError));
                ValidateFinite(result.RandomEffects.PredictionInterval.CriticalValue, nameof(result.RandomEffects.PredictionInterval.CriticalValue));
                ValidateFinite(result.RandomEffects.PredictionInterval.AnalysisScaleEffect, nameof(result.RandomEffects.PredictionInterval.AnalysisScaleEffect));
                ValidateFinite(result.RandomEffects.PredictionInterval.AnalysisScaleLower, nameof(result.RandomEffects.PredictionInterval.AnalysisScaleLower));
                ValidateFinite(result.RandomEffects.PredictionInterval.AnalysisScaleUpper, nameof(result.RandomEffects.PredictionInterval.AnalysisScaleUpper));
                ValidateFinite(result.RandomEffects.PredictionInterval.ReportedScaleEffect, nameof(result.RandomEffects.PredictionInterval.ReportedScaleEffect));
                ValidateFinite(result.RandomEffects.PredictionInterval.ReportedScaleLower, nameof(result.RandomEffects.PredictionInterval.ReportedScaleLower));
                ValidateFinite(result.RandomEffects.PredictionInterval.ReportedScaleUpper, nameof(result.RandomEffects.PredictionInterval.ReportedScaleUpper));
            }
        }

        ValidateContributions(result.Contributions);
    }

    private static void ValidateContributions(IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        foreach (var contribution in contributions)
        {
            ValidateFinite(contribution.AnalysisScaleEffect, nameof(contribution.AnalysisScaleEffect));
            ValidateFinite(contribution.AnalysisScaleVariance, nameof(contribution.AnalysisScaleVariance));
            ValidateFinite(contribution.AnalysisScaleStandardError, nameof(contribution.AnalysisScaleStandardError));
            ValidateFinite(contribution.Weight, nameof(contribution.Weight));
            ValidateFinite(contribution.NormalizedWeight, nameof(contribution.NormalizedWeight));
        }
    }

    private static void ValidateFinite(double? value, string name)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            throw new InvalidOperationException($"Quantitative synthesis artifact contains a non-finite value in {name}.");
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException($"Quantitative synthesis artifact contains a non-finite value in {name}.");
        }
    }
}

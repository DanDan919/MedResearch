using System.Text.Json;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class EfQuantitativeSynthesisArtifactStore : IQuantitativeSynthesisArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly MedResearchDbContext _dbContext;

    public EfQuantitativeSynthesisArtifactStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task PersistAsync(QuantitativeSynthesisReadiness readiness, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        if (readiness.ResearchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(readiness));
        }

        var results = readiness.Results
            .OrderBy(result => result.GroupKey, StringComparer.Ordinal)
            .ToArray();
        if (results.Any(result => result.ResearchRunId != readiness.ResearchRunId))
        {
            throw new InvalidOperationException("Quantitative synthesis result does not belong to the readiness research run.");
        }
        var snapshots = results
            .Select(result => new Snapshot(result, QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result)))
            .ToArray();

        await ValidateLineageAsync(readiness.ResearchRunId, results, cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var groupKeys = snapshots.Select(snapshot => snapshot.Result.GroupKey).ToArray();
            var existing = await _dbContext.QuantitativeSynthesisArtifacts
                .Where(artifact => artifact.ResearchRunId == readiness.ResearchRunId && groupKeys.Contains(artifact.GroupKey))
                .ToDictionaryAsync(artifact => artifact.GroupKey, cancellationToken);

            foreach (var snapshot in snapshots)
            {
                if (existing.TryGetValue(snapshot.Result.GroupKey, out var existingArtifact))
                {
                    if (!string.Equals(existingArtifact.SnapshotFingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Quantitative synthesis artifact '{snapshot.Result.GroupKey}' already exists with a different deterministic snapshot.");
                    }

                    continue;
                }

                var artifact = new QuantitativeSynthesisArtifactEntity
                {
                    Id = Guid.NewGuid(),
                    ResearchRunId = readiness.ResearchRunId,
                    GroupKey = snapshot.Result.GroupKey,
                    Status = snapshot.Result.Status,
                    AlgorithmVersion = snapshot.Result.AlgorithmVersion,
                    OutputConfidenceLevel = snapshot.Result.OutputConfidenceLevel,
                    EvidenceCount = snapshot.Result.EvidenceCount,
                    UniqueStudyCount = snapshot.Result.UniqueStudyCount,
                    SnapshotFingerprint = snapshot.Fingerprint,
                    SnapshotJson = JsonSerializer.Serialize(snapshot.Result, JsonOptions),
                    PersistedAt = DateTimeOffset.UtcNow
                };
                _dbContext.QuantitativeSynthesisArtifacts.Add(artifact);

                AddContributionSnapshots(artifact.Id, "fixed-effect", snapshot.Result.Contributions);
                if (snapshot.Result.RandomEffects is not null)
                {
                    AddContributionSnapshots(artifact.Id, "random-effects", snapshot.Result.RandomEffects.Contributions);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await VerifyExistingSnapshotsAsync(readiness.ResearchRunId, snapshots, cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> FindByResearchRunIdAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        var entities = await _dbContext.QuantitativeSynthesisArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.ResearchRunId == researchRunId)
            .OrderBy(artifact => artifact.GroupKey)
            .ThenBy(artifact => artifact.Id)
            .ToArrayAsync(cancellationToken);

        return entities
            .Select(entity => new QuantitativeSynthesisArtifactReadModel(
                entity.Id,
                entity.PersistedAt,
                entity.SnapshotFingerprint,
                JsonSerializer.Deserialize<QuantitativeSynthesisResult>(entity.SnapshotJson, JsonOptions)
                    ?? throw new InvalidOperationException("Persisted quantitative synthesis snapshot is invalid.")))
            .ToArray();
    }

    private async Task ValidateLineageAsync(
        Guid researchRunId,
        IReadOnlyCollection<QuantitativeSynthesisResult> results,
        CancellationToken cancellationToken)
    {
        var contributions = results
            .SelectMany(result => result.Contributions.Concat(result.RandomEffects?.Contributions ?? []))
            .ToArray();
        if (contributions.Length == 0)
        {
            return;
        }

        var evidenceIds = contributions.Select(contribution => contribution.EvidenceId).Distinct().ToArray();
        var evidence = await _dbContext.Evidence
            .AsNoTracking()
            .Where(item => evidenceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var extractionIds = contributions.Select(contribution => contribution.EvidenceExtractionId).Distinct().ToArray();
        var extractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(item => extractionIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var sourceMaterialIds = contributions.Select(contribution => contribution.SourceMaterialId).Distinct().ToArray();
        var sourceMaterials = await _dbContext.SourceMaterials
            .AsNoTracking()
            .Where(item => sourceMaterialIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var contribution in contributions)
        {
            if (!evidence.TryGetValue(contribution.EvidenceId, out var item)
                || item.ResearchRunId != researchRunId
                || item.StudyId != contribution.StudyId
                || item.EvidenceExtractionId != contribution.EvidenceExtractionId)
            {
                throw new InvalidOperationException("Quantitative contribution evidence does not belong to the analyzed research run and lineage.");
            }

            if (!extractions.TryGetValue(contribution.EvidenceExtractionId, out var extraction)
                || extraction.ResearchRunId != researchRunId
                || extraction.StudyId != contribution.StudyId)
            {
                throw new InvalidOperationException("Quantitative contribution extraction does not belong to the analyzed research run and lineage.");
            }

            if (!sourceMaterials.TryGetValue(contribution.SourceMaterialId, out var sourceMaterial)
                || sourceMaterial.StudyId != contribution.StudyId)
            {
                throw new InvalidOperationException("Quantitative contribution source material does not belong to the cited Study.");
            }
        }
    }

    private async Task VerifyExistingSnapshotsAsync(
        Guid researchRunId,
        IReadOnlyCollection<Snapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var groupKeys = snapshots.Select(snapshot => snapshot.Result.GroupKey).ToArray();
        var existing = await _dbContext.QuantitativeSynthesisArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.ResearchRunId == researchRunId && groupKeys.Contains(artifact.GroupKey))
            .ToDictionaryAsync(artifact => artifact.GroupKey, cancellationToken);

        foreach (var snapshot in snapshots)
        {
            if (!existing.TryGetValue(snapshot.Result.GroupKey, out var artifact)
                || !string.Equals(artifact.SnapshotFingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Concurrent quantitative synthesis persistence produced a conflicting snapshot.");
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    private void AddContributionSnapshots(
        Guid artifactId,
        string analysisMethod,
        IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        foreach (var item in contributions
            .Select((contribution, ordinal) => (contribution, ordinal))
            .OrderBy(item => item.contribution.StudyId)
            .ThenBy(item => item.contribution.EvidenceId))
        {
            _dbContext.QuantitativeSynthesisContributionSnapshots.Add(new QuantitativeSynthesisContributionSnapshotEntity
            {
                ArtifactId = artifactId,
                AnalysisMethod = analysisMethod,
                Ordinal = item.ordinal,
                EvidenceId = item.contribution.EvidenceId,
                StudyId = item.contribution.StudyId,
                EvidenceExtractionId = item.contribution.EvidenceExtractionId,
                SourceMaterialId = item.contribution.SourceMaterialId,
                AnalysisScaleEffect = item.contribution.AnalysisScaleEffect,
                AnalysisScaleVariance = item.contribution.AnalysisScaleVariance,
                AnalysisScaleStandardError = item.contribution.AnalysisScaleStandardError,
                Weight = item.contribution.Weight,
                NormalizedWeight = item.contribution.NormalizedWeight
            });
        }
    }

    private sealed record Snapshot(QuantitativeSynthesisResult Result, string Fingerprint);
}

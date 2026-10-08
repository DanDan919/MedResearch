using System.Text.Json;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MedResearch.Application.Security;
using Npgsql;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class EfQuantitativeSynthesisArtifactStore : IQuantitativeSynthesisArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly MedResearchDbContext _dbContext;
    private readonly IResearchRunWriteFence? _writeFence;

    public EfQuantitativeSynthesisArtifactStore(
        MedResearchDbContext dbContext,
        IResearchRunWriteFence? writeFence = null)
    {
        _dbContext = dbContext;
        _writeFence = writeFence;
    }

    public async Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> PersistAsync(QuantitativeSynthesisReadiness readiness, CancellationToken cancellationToken)
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
            if (_writeFence is not null)
            {
                await _writeFence.AssertOwnedAsync(readiness.ResearchRunId, cancellationToken);
            }

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
        var persistedKeys = results.Select(result => result.GroupKey).ToArray();
        var persisted = await _dbContext.QuantitativeSynthesisArtifacts.AsNoTracking()
            .Where(artifact => artifact.ResearchRunId == readiness.ResearchRunId && persistedKeys.Contains(artifact.GroupKey))
            .OrderBy(artifact => artifact.GroupKey).ToArrayAsync(cancellationToken);
        return await ReadModelsAsync(persisted, readiness.ResearchRunId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> FindByResearchRunIdAsync(
        Guid researchRunId,
        string ownerSubjectId,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        ownerSubjectId = ActorIdentity.NormalizeSubject(ownerSubjectId);

        var entities = await _dbContext.QuantitativeSynthesisArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.ResearchRunId == researchRunId)
            .Join(
                _dbContext.ResearchRuns.AsNoTracking(),
                artifact => artifact.ResearchRunId,
                run => run.Id,
                (artifact, run) => new { artifact, run.ResearchQuestionId })
            .Join(
                _dbContext.ResearchQuestions.AsNoTracking().Where(question => question.OwnerSubjectId == ownerSubjectId),
                item => item.ResearchQuestionId,
                question => question.Id,
                (item, _) => item.artifact)
            .OrderBy(artifact => artifact.GroupKey)
            .ThenBy(artifact => artifact.Id)
            .ToArrayAsync(cancellationToken);

        return await ReadModelsAsync(entities, researchRunId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> ReadModelsAsync(
        QuantitativeSynthesisArtifactEntity[] entities, Guid researchRunId, CancellationToken cancellationToken)
    {
        var artifactIds = entities.Select(entity => entity.Id).ToArray();
        var contributionRows = artifactIds.Length == 0
            ? []
            : await _dbContext.QuantitativeSynthesisContributionSnapshots
                .AsNoTracking()
                .Where(snapshot => artifactIds.Contains(snapshot.ArtifactId))
                .OrderBy(snapshot => snapshot.ArtifactId)
                .ThenBy(snapshot => snapshot.AnalysisMethod)
                .ThenBy(snapshot => snapshot.Ordinal)
                .ToArrayAsync(cancellationToken);

        var readModels = new List<QuantitativeSynthesisArtifactReadModel>(entities.Length);
        foreach (var entity in entities)
        {
            var result = JsonSerializer.Deserialize<QuantitativeSynthesisResult>(entity.SnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("Persisted quantitative synthesis snapshot is invalid.");

            ValidateRelationalSnapshot(entity, result, contributionRows.Where(row => row.ArtifactId == entity.Id).ToArray(), researchRunId);
            readModels.Add(new QuantitativeSynthesisArtifactReadModel(
                entity.Id,
                entity.PersistedAt,
                entity.SnapshotFingerprint,
                result));
        }

        return readModels;
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
                || extraction.StudyId != contribution.StudyId
                || extraction.SourceMaterialId != contribution.SourceMaterialId
                || extraction.Status != EvidenceExtractionStatus.Completed
                || !extraction.GroundingValidated)
            {
                throw new InvalidOperationException("Quantitative contribution extraction does not belong to the analyzed research run and lineage.");
            }

            if (!sourceMaterials.TryGetValue(contribution.SourceMaterialId, out var sourceMaterial)
                || sourceMaterial.StudyId != contribution.StudyId
                || !item.GroundingValidated)
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
            .OrderBy(contribution => contribution.StudyId)
            .ThenBy(contribution => contribution.EvidenceId)
            .Select((contribution, ordinal) => (contribution, ordinal)))
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

    private static void ValidateRelationalSnapshot(
        QuantitativeSynthesisArtifactEntity entity,
        QuantitativeSynthesisResult result,
        IReadOnlyCollection<QuantitativeSynthesisContributionSnapshotEntity> rows,
        Guid researchRunId)
    {
        if (result.ResearchRunId != researchRunId
            || result.ResearchRunId != entity.ResearchRunId
            || !string.Equals(result.GroupKey, entity.GroupKey, StringComparison.Ordinal)
            || !string.Equals(QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result), entity.SnapshotFingerprint, StringComparison.Ordinal)
            || result.Status != entity.Status
            || !string.Equals(result.AlgorithmVersion, entity.AlgorithmVersion, StringComparison.Ordinal)
            || result.OutputConfidenceLevel != entity.OutputConfidenceLevel
            || result.EvidenceCount != entity.EvidenceCount
            || result.UniqueStudyCount != entity.UniqueStudyCount)
        {
            throw new InvalidOperationException("Persisted quantitative synthesis JSON is inconsistent with its relational snapshot.");
        }

        ValidateContributionRows(rows, "fixed-effect", result.Contributions);
        ValidateContributionRows(rows, "random-effects", result.RandomEffects?.Contributions ?? []);

        var expectedMethods = result.RandomEffects is null
            ? new[] { "fixed-effect" }
            : new[] { "fixed-effect", "random-effects" };
        if (rows.Select(row => row.AnalysisMethod).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(expectedMethods.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal) is false)
        {
            throw new InvalidOperationException("Persisted quantitative contribution snapshot methods are inconsistent with the JSON snapshot.");
        }
    }

    private static void ValidateContributionRows(
        IReadOnlyCollection<QuantitativeSynthesisContributionSnapshotEntity> rows,
        string analysisMethod,
        IReadOnlyCollection<QuantitativeSynthesisContribution> contributions)
    {
        var actual = rows
            .Where(row => string.Equals(row.AnalysisMethod, analysisMethod, StringComparison.Ordinal))
            .OrderBy(row => row.Ordinal)
            .ToArray();
        var expected = contributions
            .OrderBy(contribution => contribution.StudyId)
            .ThenBy(contribution => contribution.EvidenceId)
            .ToArray();

        if (actual.Length != expected.Length)
        {
            throw new InvalidOperationException("Persisted quantitative contribution count is inconsistent with the JSON snapshot.");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var row = actual[index];
            var contribution = expected[index];
            if (row.Ordinal != index
                || row.EvidenceId != contribution.EvidenceId
                || row.StudyId != contribution.StudyId
                || row.EvidenceExtractionId != contribution.EvidenceExtractionId
                || row.SourceMaterialId != contribution.SourceMaterialId
                || row.AnalysisScaleEffect != contribution.AnalysisScaleEffect
                || row.AnalysisScaleVariance != contribution.AnalysisScaleVariance
                || row.AnalysisScaleStandardError != contribution.AnalysisScaleStandardError
                || row.Weight != contribution.Weight
                || row.NormalizedWeight != contribution.NormalizedWeight)
            {
                throw new InvalidOperationException("Persisted quantitative contribution lineage is inconsistent with the JSON snapshot.");
            }
        }
    }

    private sealed record Snapshot(QuantitativeSynthesisResult Result, string Fingerprint);
}

using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.SourceMaterials.Persistence;

public sealed class EfSourceMaterialStore : ISourceMaterialStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfSourceMaterialStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SourceMaterialAcquisitionStudySet> FindStudiesForSourceAcquisitionAsync(
        Guid researchRunId,
        int maxStudies,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        if (maxStudies <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStudies), "Max studies must be positive.");
        }

        var discovered = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ResearchRunId == researchRunId)
            .GroupBy(discovery => discovery.StudyId)
            .Select(group => new
            {
                StudyId = group.Key,
                DiscoveredAt = group.Min(discovery => discovery.DiscoveredAt)
            })
            .Join(
                _dbContext.Studies.AsNoTracking(),
                discovery => discovery.StudyId,
                study => study.Id,
                (discovery, study) => new { discovery, study })
            .OrderBy(item => item.discovery.DiscoveredAt)
            .ThenBy(item => item.study.Pmid)
            .ThenBy(item => item.study.Pmcid)
            .ThenBy(item => item.study.Doi)
            .ThenBy(item => item.study.Id)
            .ToArrayAsync(cancellationToken);

        var studies = discovered
            .Take(maxStudies)
            .Select(item => new SourceMaterialStudyContext(
                researchRunId,
                item.study.Id,
                item.study.Title,
                item.study.Pmid,
                item.study.Pmcid,
                item.study.Doi,
                item.study.Abstract,
                item.study.Source))
            .ToArray();

        return new SourceMaterialAcquisitionStudySet(discovered.Length, studies);
    }

    public async Task<SourceMaterialPersistenceResult> PersistSourceMaterialAsync(
        Guid studyId,
        SourceMaterialCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (studyId == Guid.Empty)
        {
            throw new ArgumentException("Study id cannot be empty.", nameof(studyId));
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var content = SourceMaterial.NormalizeContent(candidate.Content);
        var contentHash = SourceMaterial.ComputeContentHash(content);
        var providerSourceId = string.IsNullOrWhiteSpace(candidate.ProviderSourceId)
            ? null
            : string.Join(' ', candidate.ProviderSourceId.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));

        var existingSameVersion = await _dbContext.SourceMaterials
            .SingleOrDefaultAsync(material =>
                material.StudyId == studyId
                && material.Type == candidate.Type
                && material.Provider == candidate.Provider
                && material.ProviderSourceId == providerSourceId
                && material.ContentHash == contentHash,
                cancellationToken);
        if (existingSameVersion is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new SourceMaterialPersistenceResult(
                existingSameVersion.Id,
                false,
                true,
                false,
                existingSameVersion.ContentHash,
                existingSameVersion.ContentVersion);
        }

        var currentVersions = await _dbContext.SourceMaterials
            .Where(material =>
                material.StudyId == studyId
                && material.Type == candidate.Type
                && material.Provider == candidate.Provider
                && material.ProviderSourceId == providerSourceId
                && material.IsCurrent)
            .ToArrayAsync(cancellationToken);
        var nextVersion = currentVersions.Length == 0
            ? 1
            : currentVersions.Max(material => material.ContentVersion) + 1;

        foreach (var current in currentVersions)
        {
            current.MarkNotCurrent();
        }

        var sourceMaterial = SourceMaterial.Create(
            studyId,
            candidate.Type,
            candidate.Provider,
            providerSourceId,
            candidate.RetrievalMethod,
            content,
            nextVersion,
            candidate.RetrievedAt,
            candidate.SourceUpdatedAt,
            candidate.License,
            candidate.LicenseUrl,
            candidate.AccessStatus,
            candidate.WasTruncated,
            candidate.SectionNames.ToArray());

        _dbContext.SourceMaterials.Add(sourceMaterial);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SourceMaterialPersistenceResult(
            sourceMaterial.Id,
            nextVersion == 1,
            false,
            nextVersion > 1,
            sourceMaterial.ContentHash,
            sourceMaterial.ContentVersion);
    }
}

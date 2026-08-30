using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Literature.Persistence;

public sealed class EfScientificSearchResultStore : IScientificSearchResultStore
{
    private readonly MedResearchDbContext _dbContext;

    public EfScientificSearchResultStore(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
        ScientificSearchPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var persistedCount = 0;
        var duplicateCount = 0;
        var discoveredAt = DateTimeOffset.UtcNow;
        var studiesByIdentity = new Dictionary<string, Study>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in request.Candidates)
        {
            var normalizedCandidate = NormalizeCandidate(candidate);
            var identityKey = GetIdentityKey(normalizedCandidate);

            if (identityKey is not null && studiesByIdentity.TryGetValue(identityKey, out var alreadyDiscoveredStudy))
            {
                duplicateCount++;
                await AddDiscoveryIfMissingAsync(
                    request.ResearchRunId,
                    request.SearchExecutionId,
                    alreadyDiscoveredStudy.Id,
                    normalizedCandidate,
                    discoveredAt,
                    cancellationToken);
                continue;
            }

            var study = await FindExistingStudyAsync(normalizedCandidate, cancellationToken);

            if (study is null)
            {
                study = CreateStudy(normalizedCandidate);
                _dbContext.Studies.Add(study);
                persistedCount++;
            }
            else
            {
                duplicateCount++;
            }

            if (identityKey is not null)
            {
                studiesByIdentity[identityKey] = study;
            }

            await AddDiscoveryIfMissingAsync(
                request.ResearchRunId,
                request.SearchExecutionId,
                study.Id,
                normalizedCandidate,
                discoveredAt,
                cancellationToken);
        }

        _dbContext.LiteratureSearches.Add(new LiteratureSearch(
            request.SearchExecutionId,
            request.ResearchRunId,
            request.Source,
            request.Query,
            request.SearchedAt,
            request.ResultCount,
            persistedCount,
            duplicateCount));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ScientificSearchPersistenceResult(request.SearchExecutionId, persistedCount, duplicateCount);
    }

    private async Task<Study?> FindExistingStudyAsync(
        ScientificStudyCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Pmid))
        {
            var byPmid = await _dbContext.Studies
                .SingleOrDefaultAsync(study => study.Pmid == candidate.Pmid, cancellationToken);

            if (byPmid is not null)
            {
                return byPmid;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.Doi))
        {
            return await _dbContext.Studies
                .SingleOrDefaultAsync(study => study.Doi == candidate.Doi, cancellationToken);
        }

        return null;
    }

    private async Task AddDiscoveryIfMissingAsync(
        Guid researchRunId,
        Guid searchExecutionId,
        Guid studyId,
        ScientificStudyCandidate candidate,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken)
    {
        var alreadyTracked = _dbContext.ResearchStudyDiscoveries.Local.Any(
            discovery => discovery.ResearchRunId == researchRunId && discovery.StudyId == studyId);
        var alreadyPersisted = await _dbContext.ResearchStudyDiscoveries.AnyAsync(
            discovery => discovery.ResearchRunId == researchRunId && discovery.StudyId == studyId,
            cancellationToken);

        if (alreadyTracked || alreadyPersisted)
        {
            return;
        }

        _dbContext.ResearchStudyDiscoveries.Add(new ResearchStudyDiscovery(
            Guid.NewGuid(),
            researchRunId,
            searchExecutionId,
            studyId,
            candidate.Source,
            candidate.Pmid ?? candidate.Doi,
            discoveredAt));
    }

    private static Study CreateStudy(ScientificStudyCandidate candidate)
    {
        return new Study(
            Guid.NewGuid(),
            candidate.Title,
            candidate.Abstract,
            candidate.Doi,
            candidate.Pmid,
            candidate.Journal,
            candidate.PublicationDate,
            candidate.PublicationYear,
            candidate.PublicationMonth,
            candidate.PublicationDay,
            candidate.PublicationTypes.ToArray(),
            candidate.Authors.ToArray(),
            candidate.Source);
    }

    private static ScientificStudyCandidate NormalizeCandidate(ScientificStudyCandidate candidate)
    {
        return candidate with
        {
            Pmid = NormalizeIdentifier(candidate.Pmid),
            Doi = NormalizeDoi(candidate.Doi),
            PublicationTypes = NormalizeCollection(candidate.PublicationTypes),
            Authors = NormalizeCollection(candidate.Authors)
        };
    }

    private static string? GetIdentityKey(ScientificStudyCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Pmid))
        {
            return $"pmid:{candidate.Pmid}";
        }

        if (!string.IsNullOrWhiteSpace(candidate.Doi))
        {
            return $"doi:{candidate.Doi}";
        }

        return null;
    }

    private static string? NormalizeIdentifier(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeDoi(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static IReadOnlyCollection<string> NormalizeCollection(IReadOnlyCollection<string> values)
    {
        return values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}



using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.Identity;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Infrastructure.Literature.Persistence;

public sealed class EfScientificSearchResultStore : IScientificSearchResultStore
{
    private readonly MedResearchDbContext _dbContext;
    private readonly ILogger<EfScientificSearchResultStore> _logger;

    public EfScientificSearchResultStore(MedResearchDbContext dbContext)
        : this(dbContext, NullLogger<EfScientificSearchResultStore>.Instance)
    {
    }

    public EfScientificSearchResultStore(
        MedResearchDbContext dbContext,
        ILogger<EfScientificSearchResultStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
            var identityKeys = GetIdentityKeys(normalizedCandidate);

            var trackedMatches = identityKeys
                .Where(studiesByIdentity.ContainsKey)
                .Select(identityKey => studiesByIdentity[identityKey])
                .DistinctBy(study => study.Id)
                .ToArray();

            if (trackedMatches.Length > 1)
            {
                duplicateCount++;
                LogIdentityConflict(request, normalizedCandidate, "Current search batch contains identifiers already attached to different Studies.");
                continue;
            }

            if (trackedMatches.Length == 1)
            {
                duplicateCount++;
                EnrichStudyMetadata(trackedMatches[0], normalizedCandidate);
                await AddDiscoveryIfMissingAsync(
                    request.ResearchRunId,
                    request.SearchExecutionId,
                    trackedMatches[0].Id,
                    normalizedCandidate,
                    discoveredAt,
                    cancellationToken);
                continue;
            }

            await AcquireIdentityLocksAsync(identityKeys, cancellationToken);

            var resolution = await ResolveExistingStudyAsync(normalizedCandidate, cancellationToken);
            if (resolution.IsConflict)
            {
                duplicateCount++;
                LogIdentityConflict(request, normalizedCandidate, "Candidate identifiers match more than one existing Study.");
                continue;
            }

            var study = resolution.Study;
            if (study is null)
            {
                study = CreateStudy(normalizedCandidate);
                _dbContext.Studies.Add(study);
                persistedCount++;
            }
            else
            {
                duplicateCount++;
                EnrichStudyMetadata(study, normalizedCandidate);
            }

            foreach (var identityKey in identityKeys)
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
            duplicateCount,
            request.ResearchPlanId));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ScientificSearchPersistenceResult(request.SearchExecutionId, persistedCount, duplicateCount);
    }

    private async Task<StudyResolution> ResolveExistingStudyAsync(
        ScientificStudyCandidate candidate,
        CancellationToken cancellationToken)
    {
        var matches = new List<Study>();

        if (!string.IsNullOrWhiteSpace(candidate.Pmid))
        {
            var byPmid = await _dbContext.Studies
                .SingleOrDefaultAsync(study => study.Pmid == candidate.Pmid, cancellationToken);

            if (byPmid is not null)
            {
                matches.Add(byPmid);
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.Pmcid))
        {
            var byPmcid = await _dbContext.Studies
                .SingleOrDefaultAsync(study => study.Pmcid == candidate.Pmcid, cancellationToken);

            if (byPmcid is not null)
            {
                matches.Add(byPmcid);
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.Doi))
        {
            var byDoi = await _dbContext.Studies
                .SingleOrDefaultAsync(study => study.Doi == candidate.Doi, cancellationToken);

            if (byDoi is not null)
            {
                matches.Add(byDoi);
            }
        }

        var distinctMatches = matches.DistinctBy(study => study.Id).ToArray();
        return distinctMatches.Length switch
        {
            0 => StudyResolution.NotFound,
            1 => StudyResolution.Found(distinctMatches[0]),
            _ => StudyResolution.Conflict
        };
    }


    private async Task AcquireIdentityLocksAsync(
        IReadOnlyCollection<string> identityKeys,
        CancellationToken cancellationToken)
    {
        foreach (var identityKey in identityKeys.Order(StringComparer.OrdinalIgnoreCase))
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({identityKey}, 0))",
                cancellationToken);
        }
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
            discovery => discovery.ResearchRunId == researchRunId && discovery.LiteratureSearchId == searchExecutionId && discovery.StudyId == studyId);
        var alreadyPersisted = await _dbContext.ResearchStudyDiscoveries.AnyAsync(
            discovery => discovery.ResearchRunId == researchRunId && discovery.LiteratureSearchId == searchExecutionId && discovery.StudyId == studyId,
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
            candidate.ProviderRecordId ?? candidate.Pmid ?? candidate.Pmcid ?? candidate.Doi,
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
            candidate.Pmcid,
            candidate.Journal,
            candidate.PublicationDate,
            candidate.PublicationYear,
            candidate.PublicationMonth,
            candidate.PublicationDay,
            candidate.PublicationTypes.ToArray(),
            candidate.Authors.ToArray(),
            candidate.Source);
    }

    private static void EnrichStudyMetadata(Study study, ScientificStudyCandidate candidate)
    {
        study.EnrichMissingMetadata(
            candidate.Abstract,
            candidate.Doi,
            candidate.Pmid,
            candidate.Pmcid,
            candidate.Journal,
            candidate.PublicationDate,
            candidate.PublicationYear,
            candidate.PublicationMonth,
            candidate.PublicationDay,
            candidate.PublicationTypes.ToArray(),
            candidate.Authors.ToArray());
    }

    private static ScientificStudyCandidate NormalizeCandidate(ScientificStudyCandidate candidate)
    {
        return candidate with
        {
            Pmid = ScientificIdentifierNormalizer.NormalizePmid(candidate.Pmid),
            Pmcid = ScientificIdentifierNormalizer.NormalizePmcid(candidate.Pmcid),
            Doi = ScientificIdentifierNormalizer.NormalizeDoi(candidate.Doi),
            ProviderRecordId = ScientificIdentifierNormalizer.NormalizeWhitespace(candidate.ProviderRecordId),
            PublicationTypes = NormalizeCollection(candidate.PublicationTypes),
            Authors = NormalizeCollection(candidate.Authors)
        };
    }

    private static IReadOnlyCollection<string> GetIdentityKeys(ScientificStudyCandidate candidate)
    {
        var identityKeys = new List<string>(capacity: 3);

        if (!string.IsNullOrWhiteSpace(candidate.Pmid))
        {
            identityKeys.Add($"pmid:{candidate.Pmid}");
        }

        if (!string.IsNullOrWhiteSpace(candidate.Pmcid))
        {
            identityKeys.Add($"pmcid:{candidate.Pmcid}");
        }

        if (!string.IsNullOrWhiteSpace(candidate.Doi))
        {
            identityKeys.Add($"doi:{candidate.Doi}");
        }

        return identityKeys;
    }

    private static IReadOnlyCollection<string> NormalizeCollection(IReadOnlyCollection<string> values)
    {
        return values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void LogIdentityConflict(
        ScientificSearchPersistenceRequest request,
        ScientificStudyCandidate candidate,
        string reason)
    {
        _logger.LogWarning(
            "ScientificStudyIdentityConflict. ResearchRunId: {ResearchRunId}; LiteratureSearchId: {LiteratureSearchId}; Source: {Source}; HasPmid: {HasPmid}; HasPmcid: {HasPmcid}; HasDoi: {HasDoi}; Reason: {Reason}",
            request.ResearchRunId,
            request.SearchExecutionId,
            request.Source,
            !string.IsNullOrWhiteSpace(candidate.Pmid),
            !string.IsNullOrWhiteSpace(candidate.Pmcid),
            !string.IsNullOrWhiteSpace(candidate.Doi),
            reason);
    }

    private sealed record StudyResolution(Study? Study, bool IsConflict)
    {
        public static StudyResolution NotFound { get; } = new(null, false);

        public static StudyResolution Conflict { get; } = new(null, true);

        public static StudyResolution Found(Study study) => new(study, false);
    }
}

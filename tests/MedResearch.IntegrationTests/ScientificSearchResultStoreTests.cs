using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Extraction.Persistence;
using MedResearch.Infrastructure.Literature.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ScientificSearchResultStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public ScientificSearchResultStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_PersistsStudyProvenanceAndDiscoveryLink()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does PubMed persistence store discovered studies?");
        var searchExecutionId = Guid.NewGuid();
        var candidate = CreateCandidate("91000001", "10.2000/example.1");

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(searchExecutionId, runId, "sleep memory", [candidate]),
                CancellationToken.None);

            Assert.Equal(1, result.PersistedCount);
            Assert.Equal(0, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Pmid == "91000001", CancellationToken.None);
        var search = await verificationContext.LiteratureSearches.SingleAsync(search => search.Id == searchExecutionId, CancellationToken.None);
        var discovery = await verificationContext.ResearchStudyDiscoveries.SingleAsync(
            discovery => discovery.ResearchRunId == runId && discovery.StudyId == study.Id,
            CancellationToken.None);

        Assert.Equal("10.2000/example.1", study.Doi);
        Assert.Null(study.Pmcid);
        Assert.Equal("PubMed candidate title", study.Title);
        Assert.Equal("Reported abstract from PubMed.", study.Abstract);
        Assert.Equal("Journal of Examples", study.Journal);
        Assert.Equal(new DateOnly(2024, 2, 3), study.PublicationDate);
        Assert.Equal(2024, study.PublicationYear);
        Assert.Equal(2, study.PublicationMonth);
        Assert.Equal(3, study.PublicationDay);
        Assert.Equal(["Journal Article"], study.PublicationTypes);
        Assert.Equal(["Ada Lovelace"], study.Authors);
        Assert.Equal(ScientificLiteratureSourceNames.PubMed, study.Source);
        Assert.Equal(runId, search.ResearchRunId);
        Assert.Equal("sleep memory", search.Query);
        Assert.Equal(1, search.ResultCount);
        Assert.Equal(1, search.PersistedStudyCount);
        Assert.Equal(0, search.DuplicateStudyCount);
        Assert.Equal(searchExecutionId, discovery.LiteratureSearchId);
        Assert.Equal("91000001", discovery.SourceStudyIdentifier);
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_RepeatedPmidDoesNotCreateDuplicateStudyRows()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does PMID deduplication work within one search?");
        var searchExecutionId = Guid.NewGuid();
        var first = CreateCandidate("91000002", "10.2000/example.2");
        var duplicate = CreateCandidate("91000002", "10.2000/example.2");

        await using var context = _fixture.CreateDbContext();
        var store = new EfScientificSearchResultStore(context);

        var result = await store.PersistSearchResultsAsync(
            CreateRequest(searchExecutionId, runId, "duplicate pmid", [first, duplicate]),
            CancellationToken.None);

        Assert.Equal(1, result.PersistedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(1, await context.Studies.CountAsync(study => study.Pmid == "91000002", CancellationToken.None));
        Assert.Equal(1, await context.ResearchStudyDiscoveries.CountAsync(
            discovery => discovery.ResearchRunId == runId,
            CancellationToken.None));
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_ExistingDoiDoesNotCreateDuplicateStudyRows()
    {
        SkipIfPostgreSqlUnavailable();

        var firstRunId = await SeedResearchRunAsync("Does DOI dedupe work first run?");
        var secondRunId = await SeedResearchRunAsync("Does DOI dedupe work second run?");
        var doi = "10.2000/example.3";

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), firstRunId, "first doi", [CreateCandidate(null, doi)]),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), secondRunId, "second doi", [CreateCandidate(null, doi.ToUpperInvariant())]),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Doi == doi, CancellationToken.None);
        Assert.Equal(2, await verificationContext.ResearchStudyDiscoveries.CountAsync(
            discovery => discovery.StudyId == study.Id,
            CancellationToken.None));
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_SameStudyFromTwoSearchesInSameRunPreservesBothDiscoveryPaths()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does discovery provenance preserve repeated query paths?");
        var firstSearchId = Guid.NewGuid();
        var secondSearchId = Guid.NewGuid();
        var candidate = CreateCandidate("91000004", "10.2000/example.4");

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(firstSearchId, runId, "query a", [candidate]),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(secondSearchId, runId, "query b", [candidate]),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Pmid == "91000004", CancellationToken.None);
        var searches = await verificationContext.LiteratureSearches
            .Where(search => search.ResearchRunId == runId)
            .OrderBy(search => search.Query)
            .ToArrayAsync(CancellationToken.None);
        var discoveries = await verificationContext.ResearchStudyDiscoveries
            .Where(discovery => discovery.ResearchRunId == runId && discovery.StudyId == study.Id)
            .OrderBy(discovery => discovery.LiteratureSearchId)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(["query a", "query b"], searches.Select(search => search.Query).ToArray());
        Assert.Equal(2, discoveries.Length);
        Assert.Equal(new[] { firstSearchId, secondSearchId }.Order().ToArray(), discoveries.Select(discovery => discovery.LiteratureSearchId).Order().ToArray());
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_CandidatesWithoutStableIdentifiersRemainSeparateStudyRows()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Do null identifiers avoid unsafe title-based merging?");
        var searchExecutionId = Guid.NewGuid();

        await using var context = _fixture.CreateDbContext();
        var store = new EfScientificSearchResultStore(context);
        var result = await store.PersistSearchResultsAsync(
            CreateRequest(searchExecutionId, runId, "null identifiers", [CreateCandidate(null, null), CreateCandidate(null, null)]),
            CancellationToken.None);

        Assert.Equal(2, result.PersistedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(2, await context.ResearchStudyDiscoveries.CountAsync(discovery => discovery.ResearchRunId == runId, CancellationToken.None));
        Assert.Equal(2, await context.Studies.CountAsync(study => study.Pmid == null && study.Doi == null && study.Pmcid == null, CancellationToken.None));
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_SamePmidWithConflictingDoiReusesExistingStudyWithoutOverwritingMetadata()
    {
        SkipIfPostgreSqlUnavailable();

        var firstRunId = await SeedResearchRunAsync("Does PMID remain authoritative first?");
        var secondRunId = await SeedResearchRunAsync("Does conflicting DOI stay non-destructive?");
        const string pmid = "91000005";
        const string originalDoi = "10.2000/original";
        const string conflictingDoi = "10.2000/conflict";

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), firstRunId, "original", [CreateCandidate(pmid, originalDoi)]),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), secondRunId, "conflict", [CreateCandidate(pmid, conflictingDoi)]),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Pmid == pmid, CancellationToken.None);
        Assert.Equal(originalDoi, study.Doi);
        Assert.DoesNotContain(await verificationContext.Studies.ToArrayAsync(CancellationToken.None), study => study.Doi == conflictingDoi);
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_SameStudyFromPubMedAndEuropePmcPreservesSourceSpecificDiscoveryPaths()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does multi-source provenance preserve duplicate publication paths?");
        var pubMedSearchId = Guid.NewGuid();
        var europePmcSearchId = Guid.NewGuid();
        const string pmid = "91000006";
        const string doi = "10.2000/example.6";
        const string pmcid = "PMC91000006";

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(
                    pubMedSearchId,
                    runId,
                    "multi source",
                    [CreateCandidate(pmid, doi, source: ScientificLiteratureSourceNames.PubMed)],
                    ScientificLiteratureSourceNames.PubMed),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(
                    europePmcSearchId,
                    runId,
                    "multi source",
                    [CreateCandidate(pmid, $"https://doi.org/{doi.ToUpperInvariant()}", pmcid, ScientificLiteratureSourceNames.EuropePmc, "MED:91000006")],
                    ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Pmid == pmid, CancellationToken.None);
        var searches = await verificationContext.LiteratureSearches
            .Where(search => search.ResearchRunId == runId)
            .OrderBy(search => search.Source)
            .ToArrayAsync(CancellationToken.None);
        var discoveries = await verificationContext.ResearchStudyDiscoveries
            .Where(discovery => discovery.ResearchRunId == runId && discovery.StudyId == study.Id)
            .OrderBy(discovery => discovery.Source)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(doi, study.Doi);
        Assert.Equal(pmcid, study.Pmcid);
        Assert.Equal(1, await verificationContext.Studies.CountAsync(study => study.Pmid == pmid, CancellationToken.None));
        Assert.Equal([ScientificLiteratureSourceNames.EuropePmc, ScientificLiteratureSourceNames.PubMed], searches.Select(search => search.Source).ToArray());
        Assert.Equal([ScientificLiteratureSourceNames.EuropePmc, ScientificLiteratureSourceNames.PubMed], discoveries.Select(discovery => discovery.Source).ToArray());
        Assert.Contains(discoveries, discovery => discovery.Source == ScientificLiteratureSourceNames.EuropePmc && discovery.SourceStudyIdentifier == "MED:91000006");
        Assert.Contains(discoveries, discovery => discovery.Source == ScientificLiteratureSourceNames.PubMed && discovery.SourceStudyIdentifier == pmid);
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_PmcidMatchesExistingStudyWhenPmidIsAbsent()
    {
        SkipIfPostgreSqlUnavailable();

        var firstRunId = await SeedResearchRunAsync("Does PMCID create a stable identity?");
        var secondRunId = await SeedResearchRunAsync("Does PMCID dedupe without PMID?");
        const string pmcid = "PMC91000007";

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), firstRunId, "first pmcid", [CreateCandidate(null, null, pmcid)], ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), secondRunId, "second pmcid", [CreateCandidate(null, null, pmcid.ToLowerInvariant())], ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Equal(1, await verificationContext.Studies.CountAsync(study => study.Pmcid == pmcid, CancellationToken.None));
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_StableIdentifiersPointingAtDifferentStudiesAreSkippedAsHardConflict()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does hard identity conflict avoid unsafe merge?");
        var firstStudySearchId = Guid.NewGuid();
        var secondStudySearchId = Guid.NewGuid();
        var conflictSearchId = Guid.NewGuid();

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(firstStudySearchId, runId, "first", [CreateCandidate("91000008", "10.2000/a")]),
                CancellationToken.None);
            await store.PersistSearchResultsAsync(
                CreateRequest(secondStudySearchId, runId, "second", [CreateCandidate("91000009", "10.2000/b", "PMC91000009")], ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            var result = await store.PersistSearchResultsAsync(
                CreateRequest(conflictSearchId, runId, "conflict", [CreateCandidate("91000008", "10.2000/b", "PMC91000009", ScientificLiteratureSourceNames.EuropePmc, "MED:91000008")], ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);

            Assert.Equal(0, result.PersistedCount);
            Assert.Equal(1, result.DuplicateCount);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Equal(2, await verificationContext.Studies.CountAsync(study => study.Pmid == "91000008" || study.Pmid == "91000009", CancellationToken.None));
        Assert.Equal(0, await verificationContext.ResearchStudyDiscoveries.CountAsync(discovery => discovery.LiteratureSearchId == conflictSearchId, CancellationToken.None));
        var conflictSearch = await verificationContext.LiteratureSearches.SingleAsync(search => search.Id == conflictSearchId, CancellationToken.None);
        Assert.Equal(0, conflictSearch.PersistedStudyCount);
        Assert.Equal(1, conflictSearch.DuplicateStudyCount);
    }

    [SkippableFact]
    public async Task FindStudiesForExtractionAsync_MultipleSourceDiscoveriesProduceOneStudyWorkItemPerRun()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does extraction dedupe multi-source discoveries?");
        const string pmid = "91000010";
        const string doi = "10.2000/example.10";

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), runId, "pubmed", [CreateCandidate(pmid, doi)], ScientificLiteratureSourceNames.PubMed),
                CancellationToken.None);
            await store.PersistSearchResultsAsync(
                CreateRequest(Guid.NewGuid(), runId, "europe pmc", [CreateCandidate(pmid, doi, "PMC91000010", ScientificLiteratureSourceNames.EuropePmc, "MED:91000010")], ScientificLiteratureSourceNames.EuropePmc),
                CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var extractionStore = new EfEvidenceExtractionStore(verificationContext);
        var workItems = await extractionStore.FindStudiesForExtractionAsync(runId, "test-prompt", 10, CancellationToken.None);

        Assert.Equal(1, workItems.TotalDiscoveredStudyCount);
        var workItem = Assert.Single(workItems.Studies);
        Assert.Equal(pmid, workItem.Pmid);
        Assert.Equal("PMC91000010", workItem.Pmcid);
    }

    [SkippableFact]
    public async Task PersistSearchResultsAsync_ConcurrentUpsertsForSameStableIdentityCreateOneCanonicalStudy()
    {
        SkipIfPostgreSqlUnavailable();

        var runId = await SeedResearchRunAsync("Does concurrent source upsert preserve one canonical Study?");
        const string pmid = "91000011";
        const string doi = "10.2000/example.11";
        var pubMedSearchId = Guid.NewGuid();
        var europePmcSearchId = Guid.NewGuid();

        async Task<ScientificSearchPersistenceResult> PersistAsync(Guid searchExecutionId, string source, string? pmcid, string? providerRecordId)
        {
            await using var context = _fixture.CreateDbContext();
            var store = new EfScientificSearchResultStore(context);
            return await store.PersistSearchResultsAsync(
                CreateRequest(
                    searchExecutionId,
                    runId,
                    "concurrent identity",
                    [CreateCandidate(pmid, doi, pmcid, source, providerRecordId)],
                    source),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(
            PersistAsync(pubMedSearchId, ScientificLiteratureSourceNames.PubMed, null, pmid),
            PersistAsync(europePmcSearchId, ScientificLiteratureSourceNames.EuropePmc, "PMC91000011", "MED:91000011"));

        await using var verificationContext = _fixture.CreateDbContext();
        var study = await verificationContext.Studies.SingleAsync(study => study.Pmid == pmid, CancellationToken.None);
        var discoveries = await verificationContext.ResearchStudyDiscoveries
            .Where(discovery => discovery.ResearchRunId == runId && discovery.StudyId == study.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(1, results.Sum(result => result.PersistedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Equal("PMC91000011", study.Pmcid);
        Assert.Equal(2, discoveries.Length);
        Assert.Equal(1, await verificationContext.Studies.CountAsync(study => study.Pmid == pmid, CancellationToken.None));
    }
    private async Task<Guid> SeedResearchRunAsync(string questionText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    private static ScientificSearchPersistenceRequest CreateRequest(
        Guid searchExecutionId,
        Guid researchRunId,
        string query,
        IReadOnlyCollection<ScientificStudyCandidate> candidates,
        string source = ScientificLiteratureSourceNames.PubMed)
    {
        return new ScientificSearchPersistenceRequest(
            searchExecutionId,
            researchRunId,
            null,
            source,
            query,
            DateTimeOffset.UtcNow,
            candidates.Count,
            candidates);
    }

    private static ScientificStudyCandidate CreateCandidate(
        string? pmid,
        string? doi,
        string? pmcid = null,
        string source = ScientificLiteratureSourceNames.PubMed,
        string? providerRecordId = null)
    {
        return new ScientificStudyCandidate(
            pmid,
            pmcid,
            doi,
            $"{source} candidate title",
            $"Reported abstract from {source}.",
            "Journal of Examples",
            new DateOnly(2024, 2, 3),
            2024,
            2,
            3,
            ["Journal Article"],
            ["Ada Lovelace"],
            providerRecordId ?? pmid ?? pmcid ?? doi,
            source);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}

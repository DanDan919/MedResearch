using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
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
        Assert.Equal("PubMed candidate title", study.Title);
        Assert.Equal("Reported abstract from PubMed.", study.Abstract);
        Assert.Equal("Journal of Examples", study.Journal);
        Assert.Equal(new DateOnly(2024, 2, 3), study.PublicationDate);
        Assert.Equal(2024, study.PublicationYear);
        Assert.Equal(2, study.PublicationMonth);
        Assert.Equal(3, study.PublicationDay);
        Assert.Equal(["Journal Article"], study.PublicationTypes);
        Assert.Equal(["Ada Lovelace"], study.Authors);
        Assert.Equal("PubMed", study.Source);
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
        IReadOnlyCollection<ScientificStudyCandidate> candidates)
    {
        return new ScientificSearchPersistenceRequest(
            searchExecutionId,
            researchRunId,
            null,
            "PubMed",
            query,
            DateTimeOffset.UtcNow,
            candidates.Count,
            candidates);
    }

    private static ScientificStudyCandidate CreateCandidate(string? pmid, string? doi)
    {
        return new ScientificStudyCandidate(
            pmid,
            doi,
            "PubMed candidate title",
            "Reported abstract from PubMed.",
            "Journal of Examples",
            new DateOnly(2024, 2, 3),
            2024,
            2,
            3,
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}

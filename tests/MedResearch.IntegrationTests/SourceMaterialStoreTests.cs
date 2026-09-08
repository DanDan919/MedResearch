using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Domain;
using MedResearch.Infrastructure.Extraction.Persistence;
using MedResearch.Infrastructure.SourceMaterials.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SourceMaterialStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public SourceMaterialStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistSourceMaterialAsync_PreservesHistoricalVersionsAndMarksLatestCurrent()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Abstract version one.");
        await using var context = _fixture.CreateDbContext();
        var store = new EfSourceMaterialStore(context);

        var first = await store.PersistSourceMaterialAsync(seed.StudyId, Candidate("Abstract version one."), CancellationToken.None);
        var reused = await store.PersistSourceMaterialAsync(seed.StudyId, Candidate("Abstract version one."), CancellationToken.None);
        var second = await store.PersistSourceMaterialAsync(seed.StudyId, Candidate("Abstract version two."), CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(reused.Reused);
        Assert.True(second.NewVersionCreated);
        Assert.Equal(2, second.ContentVersion);

        var versions = await context.SourceMaterials
            .Where(material => material.StudyId == seed.StudyId && material.Type == SourceMaterialType.Abstract)
            .OrderBy(material => material.ContentVersion)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, versions.Length);
        Assert.False(versions[0].IsCurrent);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal("Abstract version one.", versions[0].Content);
        Assert.Equal("Abstract version two.", versions[1].Content);
    }

    [SkippableFact]
    public async Task FindStudiesForSourceAcquisitionAsync_DeduplicatesMultipleDiscoveryPaths()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Abstract text.");
        await using (var context = _fixture.CreateDbContext())
        {
            var secondSearch = new LiteratureSearch(Guid.NewGuid(), seed.RunId, "EuropePmc", "sleep recall", DateTimeOffset.UtcNow.AddSeconds(1), 1, 0, 1);
            var secondDiscovery = new ResearchStudyDiscovery(Guid.NewGuid(), seed.RunId, secondSearch.Id, seed.StudyId, "EuropePmc", "PMC123456", DateTimeOffset.UtcNow.AddSeconds(1));
            context.LiteratureSearches.Add(secondSearch);
            context.ResearchStudyDiscoveries.Add(secondDiscovery);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfSourceMaterialStore(context);
            var studies = await store.FindStudiesForSourceAcquisitionAsync(seed.RunId, 10, CancellationToken.None);

            Assert.Equal(1, studies.TotalDiscoveredStudyCount);
            var study = Assert.Single(studies.Studies);
            Assert.Equal(seed.StudyId, study.StudyId);
            Assert.Equal("PMC123456", study.Pmcid);
        }
    }

    [SkippableFact]
    public async Task FindStudiesForExtractionAsync_SelectsStructuredFullTextOverAbstractAndPersistsSourceLink()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Abstract says recall improved.");
        await using (var context = _fixture.CreateDbContext())
        {
            var sourceStore = new EfSourceMaterialStore(context);
            await sourceStore.PersistSourceMaterialAsync(seed.StudyId, Candidate("Abstract says recall improved."), CancellationToken.None);
            await sourceStore.PersistSourceMaterialAsync(seed.StudyId, new SourceMaterialCandidate(
                SourceMaterialType.StructuredFullText,
                "EuropePmc",
                "PMC123456",
                "EuropePmcFullTextXml",
                "## Methods\nRandomized methods.\n\n## Results\nFull text says recall improved in 120 adults.",
                DateTimeOffset.UtcNow,
                null,
                "CC BY",
                "https://creativecommons.org/licenses/by/4.0/",
                SourceMaterialAccessStatus.OpenAccess,
                false,
                ["Methods", "Results"]), CancellationToken.None);
        }

        Guid selectedSourceMaterialId;
        await using (var context = _fixture.CreateDbContext())
        {
            var extractionStore = new EfEvidenceExtractionStore(context);
            var workItems = await extractionStore.FindStudiesForExtractionAsync(seed.RunId, EvidenceExtractionPrompt.Version, 10, CancellationToken.None);
            var study = Assert.Single(workItems.Studies);

            Assert.Equal(EvidenceSourceScope.StructuredFullText, study.SourceScope);
            Assert.Equal("EuropePmc", study.SourceProvider);
            Assert.Contains("Full text says recall improved", study.SourceContent, StringComparison.Ordinal);
            selectedSourceMaterialId = study.SourceMaterialId!.Value;

            await extractionStore.PersistExtractionResultAsync(new EvidenceExtractionResult(
                seed.RunId,
                seed.StudyId,
                selectedSourceMaterialId,
                EvidenceExtractionStatus.Completed,
                null,
                study.SourceScope,
                "FakeLLM",
                "fake-model",
                EvidenceExtractionPrompt.Version,
                DateTimeOffset.UtcNow,
                true,
                [new AcceptedEvidenceFinding("recall", "Recall improved.", "Full text says recall improved in 120 adults.", EvidenceDirection.Positive, null, null, null, null, 120, null, null, null, null, null)]), CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var extraction = await context.EvidenceExtractions.SingleAsync(extraction => extraction.ResearchRunId == seed.RunId, CancellationToken.None);
            Assert.Equal(selectedSourceMaterialId, extraction.SourceMaterialId);
            Assert.Equal(EvidenceSourceScope.StructuredFullText, extraction.SourceScope);
        }
    }

    private async Task<SeededStudy> SeedDiscoveredStudyAsync(string abstractText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does sleep improve recall?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var plan = new ResearchPlan(Guid.NewGuid(), run.Id, question.Id, question.Text, "adults", "sleep", null, ["recall"], ["controlled trial"], ["sleep recall"], [], "FakeLLM", "fake-model", "research-planner-v1", DateTimeOffset.UtcNow);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, plan.Id);
        var study = new Study(Guid.NewGuid(), "Sleep and recall", abstractText, $"10.9090/{Guid.NewGuid():N}", RandomPmid(), "PMC123456", "Journal", new DateOnly(2026, 1, 1), 2026, 1, 1, ["Journal Article"], ["Ada Lovelace"], "PubMed");
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, study.Id, "PubMed", study.Pmid, DateTimeOffset.UtcNow);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.ResearchPlans.Add(plan);
        context.LiteratureSearches.Add(search);
        context.Studies.Add(study);
        context.ResearchStudyDiscoveries.Add(discovery);
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededStudy(run.Id, study.Id);
    }

    private static SourceMaterialCandidate Candidate(string content)
    {
        return new SourceMaterialCandidate(
            SourceMaterialType.Abstract,
            "PubMed",
            "12345678",
            "SearchMetadataAbstract",
            content,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            SourceMaterialAccessStatus.Unknown,
            false,
            ["Abstract"]);
    }

    private static string RandomPmid()
    {
        return Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }

    private sealed record SeededStudy(Guid RunId, Guid StudyId);
}

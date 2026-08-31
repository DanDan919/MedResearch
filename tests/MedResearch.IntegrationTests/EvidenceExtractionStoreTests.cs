using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;
using MedResearch.Infrastructure.Extraction.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EvidenceExtractionStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public EvidenceExtractionStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_PersistsProvenanceAndEvidenceFinding()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does sleep improve recall?", "Recall improved after sleep in 120 adults.");
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, [CreateFinding("recall", "Recall improved after sleep in 120 adults.")]);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            await store.PersistExtractionResultAsync(result, CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var extraction = await verification.EvidenceExtractions.SingleAsync(extraction => extraction.ResearchRunId == seed.RunId);
        var evidence = await verification.Evidence.SingleAsync(evidence => evidence.EvidenceExtractionId == extraction.Id);

        Assert.Equal(seed.StudyId, extraction.StudyId);
        Assert.Equal(EvidenceExtractionStatus.Completed, extraction.Status);
        Assert.Equal("FakeLLM", extraction.Provider);
        Assert.Equal("fake-model", extraction.Model);
        Assert.Equal(EvidenceExtractionPrompt.Version, extraction.PromptVersion);
        Assert.True(extraction.GroundingValidated);
        Assert.Equal(1, extraction.EvidenceCount);
        Assert.Equal(seed.RunId, evidence.ResearchRunId);
        Assert.Equal(seed.StudyId, evidence.StudyId);
        Assert.Equal("Recall improved after sleep in 120 adults.", evidence.SupportingText);
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_AllowsMultipleFindingsForOneStudy()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does sleep affect memory outcomes?", "Recall improved after sleep. Attention did not clearly change.");
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, [
            CreateFinding("recall", "Recall improved after sleep."),
            CreateFinding("attention", "Attention did not clearly change.", EvidenceDirection.NoClearEffect)
        ]);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceExtractionStore(context);
        await store.PersistExtractionResultAsync(result, CancellationToken.None);

        Assert.Equal(2, await context.Evidence.CountAsync(evidence => evidence.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_AllowsSameStudyInDifferentRuns()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedDiscoveredStudyAsync("Does sleep affect first run?", "Recall improved after sleep.");
        var secondRunId = await SeedRunDiscoveryForExistingStudyAsync(first.StudyId, "Does sleep affect second run?");

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceExtractionStore(context);
        await store.PersistExtractionResultAsync(CreateCompletedResult(first.RunId, first.StudyId, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);
        await store.PersistExtractionResultAsync(CreateCompletedResult(secondRunId, first.StudyId, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);

        Assert.Equal(2, await context.EvidenceExtractions.CountAsync(extraction => extraction.StudyId == first.StudyId));
        Assert.Equal(2, await context.Evidence.CountAsync(evidence => evidence.StudyId == first.StudyId));
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_IsIdempotentForSameRunStudyAndPromptVersion()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does sleep idempotency work?", "Recall improved after sleep.");
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, [CreateFinding("recall", "Recall improved after sleep.")]);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceExtractionStore(context);
        await store.PersistExtractionResultAsync(result, CancellationToken.None);
        await store.PersistExtractionResultAsync(result, CancellationToken.None);

        Assert.Equal(1, await context.EvidenceExtractions.CountAsync(extraction => extraction.ResearchRunId == seed.RunId));
        Assert.Equal(1, await context.Evidence.CountAsync(evidence => evidence.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task FindStudiesForExtractionAsync_ExcludesAlreadyProcessedStudiesAndPreservesQuestionAndPlan()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does preserved context work?", "Recall improved after sleep.");
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            var workItems = await store.FindStudiesForExtractionAsync(seed.RunId, EvidenceExtractionPrompt.Version, 10, CancellationToken.None);

            var study = Assert.Single(workItems.Studies);
            Assert.Equal(seed.RunId, study.ResearchRunId);
            Assert.Equal(seed.StudyId, study.StudyId);
            Assert.Equal("Does preserved context work?", study.ResearchQuestion);
            Assert.Equal("adults", study.Plan?.Population);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            await store.PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            var workItems = await store.FindStudiesForExtractionAsync(seed.RunId, EvidenceExtractionPrompt.Version, 10, CancellationToken.None);

            Assert.Empty(workItems.Studies);
        }
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_PreservesNullableScientificFieldsAsNull()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does null evidence persist?", "Recall improved after sleep.");
        var finding = new AcceptedEvidenceFinding(
            "recall",
            "Recall improved after sleep.",
            "Recall improved after sleep.",
            EvidenceDirection.Positive,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceExtractionStore(context);
        await store.PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, [finding]), CancellationToken.None);

        var evidence = await context.Evidence.SingleAsync(evidence => evidence.ResearchRunId == seed.RunId);
        Assert.Null(evidence.SampleSize);
        Assert.Null(evidence.EffectValue);
        Assert.Null(evidence.PValue);
        Assert.Null(evidence.StudyDesign);
    }

    private async Task<SeededStudy> SeedDiscoveredStudyAsync(string questionText, string? abstractText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var plan = new ResearchPlan(
            Guid.NewGuid(),
            run.Id,
            question.Id,
            question.Text,
            "adults",
            "sleep",
            null,
            ["recall"],
            ["controlled trial"],
            ["sleep recall"],
            [],
            "FakeLLM",
            "fake-model",
            "research-planner-v1",
            DateTimeOffset.UtcNow);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, plan.Id);
        var study = new Study(
            Guid.NewGuid(),
            "Sleep and recall",
            abstractText,
            $"10.5555/{Guid.NewGuid():N}",
            RandomPmid(),
            "Journal",
            new DateOnly(2026, 1, 1),
            "PubMed");
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

    private async Task<Guid> SeedRunDiscoveryForExistingStudyAsync(Guid studyId, string questionText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 0, 1);
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, studyId, "PubMed", RandomPmid(), DateTimeOffset.UtcNow);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.LiteratureSearches.Add(search);
        context.ResearchStudyDiscoveries.Add(discovery);
        await context.SaveChangesAsync(CancellationToken.None);

        return run.Id;
    }

    private static EvidenceExtractionResult CreateCompletedResult(
        Guid runId,
        Guid studyId,
        IReadOnlyCollection<AcceptedEvidenceFinding> findings)
    {
        return new EvidenceExtractionResult(
            runId,
            studyId,
            EvidenceExtractionStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            "FakeLLM",
            "fake-model",
            EvidenceExtractionPrompt.Version,
            DateTimeOffset.UtcNow,
            true,
            findings);
    }

    private static AcceptedEvidenceFinding CreateFinding(
        string outcome,
        string supportingText,
        EvidenceDirection direction = EvidenceDirection.Positive)
    {
        return new AcceptedEvidenceFinding(
            outcome,
            supportingText,
            supportingText,
            direction,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
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

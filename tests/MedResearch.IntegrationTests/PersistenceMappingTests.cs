using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PersistenceMappingTests
{
    private readonly PostgreSqlFixture _fixture;

    public PersistenceMappingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ResearchQuestion_CanBePersisted()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does aerobic exercise affect hippocampal volume?", DateTimeOffset.UtcNow);

        context.ResearchQuestions.Add(question);
        await context.SaveChangesAsync();

        var savedQuestion = await context.ResearchQuestions.SingleAsync(saved => saved.Id == question.Id);

        Assert.Equal(question.Text, savedQuestion.Text);
        Assert.Equal(question.CreatedAt, savedQuestion.CreatedAt);
    }

    [SkippableFact]
    public async Task ResearchRun_CanBePersistedAndRetrieved()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does REM sleep improve procedural learning?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, DateTimeOffset.UtcNow);

        run.StartPlanning(DateTimeOffset.UtcNow.AddMinutes(1));
        run.StartSearching(DateTimeOffset.UtcNow.AddMinutes(2));

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync();

        var savedRun = await context.ResearchRuns.SingleAsync(saved => saved.Id == run.Id);

        Assert.Equal(question.Id, savedRun.ResearchQuestionId);
        Assert.Equal(ResearchRunStatus.Searching, savedRun.Status);
        Assert.NotNull(savedRun.StartedAt);
    }

    [SkippableFact]
    public async Task Relationships_AreEnforcedByPostgreSql()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var missingQuestionRun = new ResearchRun(Guid.NewGuid(), DateTimeOffset.UtcNow);

        context.ResearchRuns.Add(missingQuestionRun);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Evidence_IsRelatedToStudyResearchRunAndExtraction()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does sleep improve recall?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var study = new Study(
            Guid.NewGuid(),
            "Sleep and memory consolidation in adults",
            "Recall improved after sleep in 120 adults.",
            "10.1234/example.doi",
            "12345678",
            "Journal of Neuroscience Examples",
            new DateOnly(2026, 1, 15),
            "PubMed");
        var extraction = new EvidenceExtraction(
            Guid.NewGuid(),
            run.Id,
            study.Id,
            EvidenceExtractionStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            "FakeLLM",
            "fake-model",
            "evidence-extractor-v1",
            DateTimeOffset.UtcNow,
            1,
            true);
        var evidence = new Evidence(
            Guid.NewGuid(),
            run.Id,
            study.Id,
            extraction.Id,
            "recall",
            "Recall improved after sleep.",
            "Recall improved after sleep in 120 adults.",
            EvidenceDirection.Positive,
            EvidenceSourceScope.Abstract,
            extraction.ExtractedAt,
            true,
            "adults",
            null,
            null,
            null,
            120,
            null,
            null,
            null,
            null,
            null);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.Studies.Add(study);
        context.EvidenceExtractions.Add(extraction);
        context.Evidence.Add(evidence);
        await context.SaveChangesAsync();

        var savedEvidence = await context.Evidence.SingleAsync(saved => saved.Id == evidence.Id);
        var savedExtraction = await context.EvidenceExtractions.SingleAsync(saved => saved.Id == savedEvidence.EvidenceExtractionId);
        var savedStudy = await context.Studies.SingleAsync(saved => saved.Id == savedEvidence.StudyId);

        Assert.Equal(run.Id, savedEvidence.ResearchRunId);
        Assert.Equal(study.Id, savedEvidence.StudyId);
        Assert.Equal(extraction.Id, savedEvidence.EvidenceExtractionId);
        Assert.Equal("Recall improved after sleep in 120 adults.", savedEvidence.SupportingText);
        Assert.Equal(120, savedEvidence.SampleSize);
        Assert.True(savedEvidence.GroundingValidated);
        Assert.Equal(EvidenceExtractionStatus.Completed, savedExtraction.Status);
        Assert.Equal(study.Title, savedStudy.Title);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}

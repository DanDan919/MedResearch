using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;
using MedResearch.Infrastructure.Evaluation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EvidenceEvaluationStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public EvidenceEvaluationStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistEvaluationResultAsync_PersistsEvaluationWithRelationshipsAndProvenance()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedEvaluableStudyAsync("Does sleep evaluation persist?", evidenceCount: 1);
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, seed.EvidenceIds);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceEvaluationStore(context);
            await store.PersistEvaluationResultAsync(result, CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var evaluation = await verification.EvidenceEvaluations.SingleAsync(evaluation => evaluation.ResearchRunId == seed.RunId);

        Assert.Equal(seed.StudyId, evaluation.StudyId);
        Assert.Equal(seed.EvidenceIds, evaluation.EvidenceIds);
        Assert.Equal(EvidenceEvaluationStatus.Completed, evaluation.Status);
        Assert.Equal("FakeLLM", evaluation.EvaluatorProvider);
        Assert.Equal("fake-model", evaluation.EvaluatorModel);
        Assert.Equal(EvidenceEvaluationPrompt.Version, evaluation.PromptVersion);
        Assert.Equal(EvidenceSourceScope.Abstract, evaluation.SourceScope);
        Assert.Equal(MethodologicalConfidence.InsufficientInformation, evaluation.OverallConfidence);
        Assert.True(evaluation.HasSampleSize);
    }

    [SkippableFact]
    public async Task PersistEvaluationResultAsync_PreservesUnknownInsufficientSourceAndNotApplicableStates()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedEvaluableStudyAsync("Do evaluation states persist?", evidenceCount: 1);
        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceEvaluationStore(context);
        await store.PersistEvaluationResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.EvidenceIds), CancellationToken.None);

        var evaluation = await context.EvidenceEvaluations.SingleAsync(evaluation => evaluation.ResearchRunId == seed.RunId);
        Assert.Equal(MethodologicalAssessmentState.Unknown, evaluation.Precision);
        Assert.Equal(MethodologicalAssessmentState.InsufficientSource, evaluation.Blinding);
        Assert.Equal(MethodologicalAssessmentState.NotApplicable, evaluation.AllocationConcealment);
        Assert.Equal(DirectnessRating.Direct, evaluation.Directness);
    }

    [SkippableFact]
    public async Task PersistEvaluationResultAsync_IsIdempotentForSameRunStudyAndPromptVersion()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedEvaluableStudyAsync("Is evaluation persistence idempotent?", evidenceCount: 1);
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, seed.EvidenceIds);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceEvaluationStore(context);
        await store.PersistEvaluationResultAsync(result, CancellationToken.None);
        await store.PersistEvaluationResultAsync(result, CancellationToken.None);

        Assert.Equal(1, await context.EvidenceEvaluations.CountAsync(evaluation => evaluation.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task PersistEvaluationResultAsync_AllowsSameStudyInDifferentResearchRuns()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedEvaluableStudyAsync("First run same study evaluation?", evidenceCount: 1);
        var second = await SeedSecondRunForExistingStudyAsync(first.StudyId);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceEvaluationStore(context);
        await store.PersistEvaluationResultAsync(CreateCompletedResult(first.RunId, first.StudyId, first.EvidenceIds), CancellationToken.None);
        await store.PersistEvaluationResultAsync(CreateCompletedResult(second.RunId, first.StudyId, second.EvidenceIds), CancellationToken.None);

        Assert.Equal(2, await context.EvidenceEvaluations.CountAsync(evaluation => evaluation.StudyId == first.StudyId));
    }

    [SkippableFact]
    public async Task FindStudiesForEvaluationAsync_LoadsEvidenceAndExcludesAlreadyEvaluatedStudies()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedEvaluableStudyAsync("Can evaluator load context?", evidenceCount: 2);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceEvaluationStore(context);
            var workItems = await store.FindStudiesForEvaluationAsync(seed.RunId, EvidenceEvaluationPrompt.Version, 10, CancellationToken.None);

            var study = Assert.Single(workItems.Studies);
            Assert.Equal(seed.RunId, study.ResearchRunId);
            Assert.Equal(seed.StudyId, study.StudyId);
            Assert.Equal(2, study.Evidence.Count);
            Assert.Equal("Can evaluator load context?", study.ResearchQuestion);
            Assert.Equal("adults", study.Plan?.Population);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceEvaluationStore(context);
            await store.PersistEvaluationResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.EvidenceIds), CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceEvaluationStore(context);
            var workItems = await store.FindStudiesForEvaluationAsync(seed.RunId, EvidenceEvaluationPrompt.Version, 10, CancellationToken.None);

            Assert.Empty(workItems.Studies);
        }
    }

    [SkippableFact]
    public async Task PersistEvaluationResultAsync_PreservesSkippedNoEvidenceEvaluation()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedEvaluableStudyAsync("Does skipped evaluation persist?", evidenceCount: 0);
        var signals = new EvidenceEvaluationSignalSet(EvidenceSourceScope.Abstract, 0, false, false, false, false, false, StudyDesignClassification.Unknown, ["No extracted source-grounded evidence findings are available for this study in the current run."]);
        var skipped = new EvidenceEvaluationResult(
            seed.RunId,
            seed.StudyId,
            [],
            EvidenceEvaluationStatus.Skipped,
            EvidenceEvaluationSkipReason.NoExtractedEvidence,
            EvidenceSourceScope.Abstract,
            null,
            null,
            EvidenceEvaluationPrompt.Version,
            DateTimeOffset.UtcNow,
            StudyDesignClassification.Unknown,
            MethodologicalAssessmentState.Unknown,
            ComparatorPresence.Unclear,
            null,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.Unclear,
            MethodologicalConfidence.InsufficientInformation,
            "No source-grounded evidence findings are available.",
            signals.ReportingLimitations,
            [],
            signals,
            4,
            5);

        await using var context = _fixture.CreateDbContext();
        var store = new EfEvidenceEvaluationStore(context);
        await store.PersistEvaluationResultAsync(skipped, CancellationToken.None);

        var evaluation = await context.EvidenceEvaluations.SingleAsync(evaluation => evaluation.ResearchRunId == seed.RunId);
        Assert.Equal(EvidenceEvaluationStatus.Skipped, evaluation.Status);
        Assert.Equal(EvidenceEvaluationSkipReason.NoExtractedEvidence, evaluation.SkipReason);
        Assert.Empty(evaluation.EvidenceIds);
        Assert.Null(evaluation.EvaluatorProvider);
    }

    private async Task<SeededEvaluationStudy> SeedEvaluableStudyAsync(string questionText, int evidenceCount)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var plan = new ResearchPlan(Guid.NewGuid(), run.Id, question.Id, question.Text, "adults", "sleep", "placebo", ["recall"], ["randomized controlled trial"], ["sleep recall"], [], "FakeLLM", "fake-model", "research-planner-v1", DateTimeOffset.UtcNow);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, plan.Id);
        var study = new Study(Guid.NewGuid(), "Sleep and recall", "A randomized trial reported improved recall in 120 adults.", $"10.7777/{Guid.NewGuid():N}", RandomPmid(), "Journal", new DateOnly(2026, 1, 1), "PubMed");
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, study.Id, "PubMed", study.Pmid, DateTimeOffset.UtcNow);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, study.Id, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, evidenceCount, true);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.ResearchPlans.Add(plan);
        context.LiteratureSearches.Add(search);
        context.Studies.Add(study);
        context.ResearchStudyDiscoveries.Add(discovery);
        context.EvidenceExtractions.Add(extraction);

        var evidenceIds = new List<Guid>();
        for (var index = 0; index < evidenceCount; index++)
        {
            var evidence = new Evidence(Guid.NewGuid(), run.Id, study.Id, extraction.Id, $"recall {index}", "Recall improved after sleep.", "reported improved recall in 120 adults", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, "adults", "sleep", "placebo", "randomized controlled trial", 120, null, null, null, null, null);
            evidenceIds.Add(evidence.Id);
            context.Evidence.Add(evidence);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return new SeededEvaluationStudy(run.Id, study.Id, evidenceIds.ToArray());
    }

    private async Task<SeededEvaluationStudy> SeedSecondRunForExistingStudyAsync(Guid studyId)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Second run same study?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 0, 1);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, studyId, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, 1, true);
        var evidence = new Evidence(Guid.NewGuid(), run.Id, studyId, extraction.Id, "recall", "Recall improved after sleep.", "reported improved recall in 120 adults", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, "adults", "sleep", "placebo", "randomized controlled trial", 120, null, null, null, null, null);
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, studyId, "PubMed", RandomPmid(), DateTimeOffset.UtcNow);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.LiteratureSearches.Add(search);
        context.ResearchStudyDiscoveries.Add(discovery);
        context.EvidenceExtractions.Add(extraction);
        context.Evidence.Add(evidence);
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededEvaluationStudy(run.Id, studyId, [evidence.Id]);
    }

    private static EvidenceEvaluationResult CreateCompletedResult(Guid runId, Guid studyId, IReadOnlyCollection<Guid> evidenceIds)
    {
        var signals = new EvidenceEvaluationSignalSet(EvidenceSourceScope.Abstract, evidenceIds.Count, true, false, false, false, true, StudyDesignClassification.RandomizedControlledTrial, ["Current source scope is abstract-level only."]);
        return new EvidenceEvaluationResult(
            runId,
            studyId,
            evidenceIds,
            EvidenceEvaluationStatus.Completed,
            null,
            EvidenceSourceScope.Abstract,
            "FakeLLM",
            "fake-model",
            EvidenceEvaluationPrompt.Version,
            DateTimeOffset.UtcNow,
            StudyDesignClassification.RandomizedControlledTrial,
            MethodologicalAssessmentState.Favorable,
            ComparatorPresence.Present,
            "placebo",
            MethodologicalAssessmentState.Favorable,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.NotApplicable,
            MethodologicalAssessmentState.InsufficientSource,
            MethodologicalAssessmentState.Unknown,
            DirectnessRating.Direct,
            MethodologicalConfidence.InsufficientInformation,
            "The evidence is direct but abstract-only methods are insufficient for full bias assessment.",
            signals.ReportingLimitations,
            [],
            signals,
            1,
            2);
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

    private sealed record SeededEvaluationStudy(Guid RunId, Guid StudyId, Guid[] EvidenceIds);
}

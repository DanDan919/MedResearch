using MedResearch.Application.Research;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchProgressStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public ResearchProgressStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task FindResearchRunProgressSnapshotAsync_ReturnsRunScopedPersistedProgress()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedCompletedProgressGraphAsync();

        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchProgressStore(context);

        var snapshot = await store.FindResearchRunProgressSnapshotAsync(seed.RunId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(seed.RunId, snapshot.ResearchRunId);
        Assert.Equal(ResearchRunStatus.Completed, snapshot.Status);
        Assert.Equal(1, snapshot.Metrics.ResearchPlanCount);
        Assert.Equal(2, snapshot.Metrics.PlannedSearchQueryCount);
        Assert.Equal(2, snapshot.Metrics.LiteratureSearchCount);
        Assert.Equal(2, snapshot.Metrics.LiteratureSearchSourceCount);
        Assert.Equal(6, snapshot.Metrics.LiteratureSearchResultCount);
        Assert.Equal(2, snapshot.Metrics.DiscoveryPathCount);
        Assert.Equal(1, snapshot.Metrics.DistinctDiscoveredStudyCount);
        Assert.Equal(2, snapshot.Metrics.CurrentSourceMaterialCount);
        Assert.Equal(1, snapshot.Metrics.StructuredFullTextMaterialCount);
        Assert.Equal(1, snapshot.Metrics.AbstractMaterialCount);
        Assert.Equal(1, snapshot.Metrics.EvidenceExtractionCount);
        Assert.Equal(1, snapshot.Metrics.CompletedEvidenceExtractionCount);
        Assert.Equal(0, snapshot.Metrics.SkippedEvidenceExtractionCount);
        Assert.Equal(1, snapshot.Metrics.EvidenceFindingCount);
        Assert.Equal(1, snapshot.Metrics.EvidenceEvaluationCount);
        Assert.Equal(1, snapshot.Metrics.CompletedEvidenceEvaluationCount);
        Assert.Equal(0, snapshot.Metrics.SkippedEvidenceEvaluationCount);
        Assert.Equal(1, snapshot.Metrics.ResearchReportCount);
        Assert.Equal(1, snapshot.Metrics.ResearchReportClaimCount);
    }

    [SkippableFact]
    public async Task FindResearchRunProgressSnapshotAsync_DoesNotCountOtherRunEvidenceForSharedStudy()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedCompletedProgressGraphAsync();

        await using (var context = _fixture.CreateDbContext())
        {
            var question = new ResearchQuestion("Does another run stay isolated?", DateTimeOffset.UtcNow);
            var otherRun = new ResearchRun(question.Id, question.CreatedAt);
            var extraction = new EvidenceExtraction(
                Guid.NewGuid(),
                otherRun.Id,
                seed.StudyId,
                seed.SourceMaterialId,
                EvidenceExtractionStatus.Completed,
                null,
                EvidenceSourceScope.Abstract,
                "FakeLLM",
                "fake-model",
                EvidenceExtractionPrompt.Version,
                DateTimeOffset.UtcNow,
                1,
                true);

            context.ResearchQuestions.Add(question);
            context.ResearchRuns.Add(otherRun);
            context.EvidenceExtractions.Add(extraction);
            context.Evidence.Add(new Evidence(
                Guid.NewGuid(),
                otherRun.Id,
                seed.StudyId,
                extraction.Id,
                "recall",
                "Other run evidence.",
                "other run support",
                EvidenceDirection.Positive,
                EvidenceSourceScope.Abstract,
                DateTimeOffset.UtcNow,
                true,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var store = new EfResearchProgressStore(verification);

        var snapshot = await store.FindResearchRunProgressSnapshotAsync(seed.RunId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.Metrics.EvidenceExtractionCount);
        Assert.Equal(1, snapshot.Metrics.EvidenceFindingCount);
    }

    private async Task<SeededProgressGraph> SeedCompletedProgressGraphAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var question = new ResearchQuestion("Does sleep improve recall?", now);
        var run = new ResearchRun(Guid.NewGuid(), question.Id, ResearchRunStatus.Completed, now, now.AddSeconds(1), now.AddMinutes(5), null);
        var plan = new ResearchPlan(
            Guid.NewGuid(),
            run.Id,
            question.Id,
            question.Text,
            "adults",
            "sleep",
            "wakefulness",
            ["recall"],
            ["controlled trial"],
            ["sleep recall", "sleep memory"],
            [],
            "FakeLLM",
            "fake-model",
            "research-planner-v1",
            now.AddSeconds(2));
        var study = new Study(
            Guid.NewGuid(),
            "Sleep and recall",
            "A trial reported improved recall.",
            $"10.9090/{Guid.NewGuid():N}",
            RandomPmid(),
            "PMC1234567",
            "Journal",
            new DateOnly(2026, 1, 1),
            2026,
            1,
            1,
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed");
        var pubMedSearch = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", now.AddSeconds(3), 2, 1, 1, plan.Id);
        var europePmcSearch = new LiteratureSearch(Guid.NewGuid(), run.Id, "EuropePmc", "sleep memory", now.AddSeconds(4), 4, 0, 1, plan.Id);
        var pubMedDiscovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, pubMedSearch.Id, study.Id, "PubMed", study.Pmid, now.AddSeconds(3));
        var europePmcDiscovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, europePmcSearch.Id, study.Id, "EuropePmc", study.Pmcid, now.AddSeconds(4));
        var abstractMaterial = SourceMaterial.Create(study.Id, SourceMaterialType.Abstract, "PubMed", study.Pmid, "SearchMetadataAbstract", study.Abstract!, 1, now.AddSeconds(5), null, null, null, SourceMaterialAccessStatus.Unknown, false, ["Abstract"]);
        var fullTextMaterial = SourceMaterial.Create(study.Id, SourceMaterialType.StructuredFullText, "EuropePmc", study.Pmcid, "EuropePmcFullTextXml", "Full text says recall improved.", 1, now.AddSeconds(6), null, "CC BY", null, SourceMaterialAccessStatus.OpenAccess, false, ["Results"]);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, study.Id, fullTextMaterial.Id, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.StructuredFullText, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, now.AddSeconds(7), 1, true);
        var evidence = new Evidence(Guid.NewGuid(), run.Id, study.Id, extraction.Id, "recall", "Recall improved.", "Full text says recall improved", EvidenceDirection.Positive, EvidenceSourceScope.StructuredFullText, now.AddSeconds(7), true, "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null);
        var evaluation = new EvidenceEvaluation(Guid.NewGuid(), run.Id, study.Id, [evidence.Id], EvidenceEvaluationStatus.Completed, null, EvidenceSourceScope.StructuredFullText, "FakeLLM", "fake-model", EvidenceEvaluationPrompt.Version, now.AddSeconds(8), StudyDesignClassification.RandomizedControlledTrial, MethodologicalAssessmentState.Favorable, ComparatorPresence.Present, "wakefulness", MethodologicalAssessmentState.Favorable, MethodologicalAssessmentState.InsufficientSource, MethodologicalAssessmentState.InsufficientSource, MethodologicalAssessmentState.InsufficientSource, MethodologicalAssessmentState.Unknown, DirectnessRating.Direct, MethodologicalConfidence.Moderate, "The source reports comparator and sample.", [], [], true, false, false, false, true, 1, 2);
        var reportId = Guid.NewGuid();
        var report = new ResearchReport(reportId, run.Id, ResearchReportStatus.Completed, null, "Executive summary.", "Evidence summary.", "Conflict summary.", "Limitations summary.", "Conclusion.", SynthesisConfidence.Limited, "FakeLLM", "fake-model", ResearchSynthesisPrompt.Version, now.AddSeconds(9), 1, 1, 1, 1, 1, 1, 1, 2, 0, 0, 1, 0, 0, false, false, false, ["PubMed", "EuropePmc"], []);
        var claim = new ResearchReportClaim(Guid.NewGuid(), reportId, ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Sleep improved recall in the cited evidence.", 0);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.ResearchPlans.Add(plan);
        context.Studies.Add(study);
        context.LiteratureSearches.AddRange(pubMedSearch, europePmcSearch);
        context.ResearchStudyDiscoveries.AddRange(pubMedDiscovery, europePmcDiscovery);
        context.SourceMaterials.AddRange(abstractMaterial, fullTextMaterial);
        context.EvidenceExtractions.Add(extraction);
        context.Evidence.Add(evidence);
        context.EvidenceEvaluations.Add(evaluation);
        context.ResearchReports.Add(report);
        context.ResearchReportClaims.Add(claim);
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededProgressGraph(run.Id, study.Id, fullTextMaterial.Id);
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

    private sealed record SeededProgressGraph(Guid RunId, Guid StudyId, Guid SourceMaterialId);
}

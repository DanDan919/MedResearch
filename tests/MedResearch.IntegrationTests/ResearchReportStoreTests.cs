using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Synthesis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchReportStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public ResearchReportStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistReportAsync_PersistsReportClaimsAndClaimEvidenceRelationships()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 2);
        var result = CreateCompletedResult(seed.RunId, seed.EvidenceIds);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await store.PersistReportAsync(result, CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var report = await verification.ResearchReports.SingleAsync(report => report.ResearchRunId == seed.RunId);
        var claims = await verification.ResearchReportClaims.Where(claim => claim.ResearchReportId == report.Id).OrderBy(claim => claim.Ordinal).ToArrayAsync();
        var links = await verification.ResearchReportClaimEvidence.Where(link => claims.Select(claim => claim.Id).Contains(link.ResearchReportClaimId)).ToArrayAsync();

        Assert.Equal(ResearchReportStatus.Completed, report.Status);
        Assert.Equal("FakeLLM", report.SynthesizerProvider);
        Assert.Equal(ResearchSynthesisPrompt.Version, report.PromptVersion);
        Assert.Equal(2, claims.Length);
        Assert.Equal(3, links.Length);
        Assert.Contains(links, link => link.EvidenceId == seed.EvidenceIds[0]);
        Assert.Contains(links, link => link.EvidenceId == seed.EvidenceIds[1]);
    }

    [SkippableFact]
    public async Task PersistReportAsync_IsIdempotentForSameRunAndPromptVersion()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        var result = CreateCompletedResult(seed.RunId, seed.EvidenceIds);
        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchSynthesisStore(context);

        await store.PersistReportAsync(result, CancellationToken.None);
        await store.PersistReportAsync(result, CancellationToken.None);

        Assert.Equal(1, await context.ResearchReports.CountAsync(report => report.ResearchRunId == seed.RunId));
        var report = await context.ResearchReports.SingleAsync(report => report.ResearchRunId == seed.RunId);
        Assert.Equal(1, await context.ResearchReportClaims.CountAsync(claim => claim.ResearchReportId == report.Id));
    }

    [SkippableFact]
    public async Task FindReportAsync_ReconstructsClaimsWithAuthoritativeCitationProjection()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await store.PersistReportAsync(CreateCompletedResult(seed.RunId, seed.EvidenceIds), CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var readStore = new EfResearchSynthesisStore(verification);
        var report = await readStore.FindReportAsync(seed.RunId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(seed.RunId, report.ResearchRunId);
        var claim = Assert.Single(report.Claims);
        var citation = Assert.Single(claim.Citations);
        Assert.Equal(seed.EvidenceIds[0], citation.EvidenceId);
        Assert.Equal(seed.StudyId, citation.StudyId);
        Assert.Equal(seed.Pmid, citation.Pmid);
        Assert.Equal(seed.Doi, citation.Doi);
        Assert.Equal("Sleep and recall report", citation.Title);
    }

    [SkippableFact]
    public async Task PersistReportAsync_PreservesInsufficientEvidenceReportWithoutClaims()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 0);
        var result = CreateInsufficientResult(seed.RunId);
        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchSynthesisStore(context);

        await store.PersistReportAsync(result, CancellationToken.None);

        var report = await context.ResearchReports.SingleAsync(report => report.ResearchRunId == seed.RunId);
        Assert.Equal(ResearchReportStatus.InsufficientEvidence, report.Status);
        Assert.Equal(ResearchReportInsufficientEvidenceReason.NoValidatedEvidence, report.InsufficientEvidenceReason);
        Assert.Equal(0, await context.ResearchReportClaims.CountAsync(claim => claim.ResearchReportId == report.Id));
    }

    [SkippableFact]
    public async Task LoadCorpusAsync_LoadsOnlyCurrentRunEvidenceForSharedStudy()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        var second = await SeedSecondRunForExistingStudyAsync(first.StudyId);
        await using var context = _fixture.CreateDbContext();
        var store = new EfResearchSynthesisStore(context);

        var corpus = await store.LoadCorpusAsync(second.RunId, CancellationToken.None);

        var evidence = Assert.Single(corpus.Evidence);
        Assert.Equal(second.RunId, evidence.ResearchRunId);
        Assert.Equal(first.StudyId, evidence.StudyId);
        Assert.Equal(second.EvidenceIds[0], evidence.EvidenceId);
    }

    private async Task<SeededRun> SeedRunWithEvidenceAsync(int evidenceCount)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does sleep improve recall?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var plan = new ResearchPlan(Guid.NewGuid(), run.Id, question.Id, question.Text, "adults", "sleep", "wakefulness", ["recall"], ["controlled trial"], ["sleep recall"], [], "FakeLLM", "fake-model", "research-planner-v1", DateTimeOffset.UtcNow);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, plan.Id);
        var doi = $"10.4242/{Guid.NewGuid():N}";
        var pmid = RandomPmid();
        var study = new Study(Guid.NewGuid(), "Sleep and recall report", "A trial reported improved recall in 120 adults.", doi, pmid, "Journal", new DateOnly(2026, 1, 1), "PubMed");
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
            var evidence = new Evidence(Guid.NewGuid(), run.Id, study.Id, extraction.Id, "recall", "Recall improved after sleep.", "reported improved recall in 120 adults", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null);
            evidenceIds.Add(evidence.Id);
            context.Evidence.Add(evidence);
        }

        if (evidenceIds.Count > 0)
        {
            context.EvidenceEvaluations.Add(CreateEvaluation(run.Id, study.Id, evidenceIds));
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return new SeededRun(run.Id, study.Id, evidenceIds.ToArray(), pmid, doi);
    }

    private async Task<SeededRun> SeedSecondRunForExistingStudyAsync(Guid studyId)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does same study stay run scoped?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 0, 1);
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, studyId, "PubMed", "12345678", DateTimeOffset.UtcNow);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, studyId, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, 1, true);
        var evidence = new Evidence(Guid.NewGuid(), run.Id, studyId, extraction.Id, "recall", "Recall improved after sleep.", "reported improved recall in 120 adults", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.LiteratureSearches.Add(search);
        context.ResearchStudyDiscoveries.Add(discovery);
        context.EvidenceExtractions.Add(extraction);
        context.Evidence.Add(evidence);
        await context.SaveChangesAsync(CancellationToken.None);
        return new SeededRun(run.Id, studyId, [evidence.Id], null, null);
    }

    private static EvidenceEvaluation CreateEvaluation(Guid runId, Guid studyId, IReadOnlyCollection<Guid> evidenceIds)
    {
        return new EvidenceEvaluation(Guid.NewGuid(), runId, studyId, evidenceIds.ToArray(), EvidenceEvaluationStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", "evidence-evaluator-v1", DateTimeOffset.UtcNow, StudyDesignClassification.RandomizedControlledTrial, MethodologicalAssessmentState.Unknown, ComparatorPresence.Present, "wakefulness", MethodologicalAssessmentState.Favorable, MethodologicalAssessmentState.InsufficientSource, MethodologicalAssessmentState.NotApplicable, MethodologicalAssessmentState.InsufficientSource, MethodologicalAssessmentState.Unknown, DirectnessRating.Direct, MethodologicalConfidence.InsufficientInformation, "Abstract-level evidence supports directness but not full methods.", ["Abstract-level only."], [], true, false, false, false, true, 1, 2);
    }

    private static ResearchSynthesisResult CreateCompletedResult(Guid runId, IReadOnlyCollection<Guid> evidenceIds)
    {
        var statistics = new SynthesisCorpusStatistics(1, 1, 1, evidenceIds.Count, 1, evidenceIds.Count, 1, 0, 1);
        var coverage = new SynthesisSourceCoverage(["PubMed"], true, false, false, false, 1);
        AcceptedResearchReportClaim[] claims = evidenceIds.Count > 1
            ? [
                new AcceptedResearchReportClaim(ResearchReportClaimType.Finding, ResearchReportClaimDirection.Positive, "Recall improved in the cited evidence.", [evidenceIds.First()], 0),
                new AcceptedResearchReportClaim(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "A cautious positive conclusion is supported by both findings.", evidenceIds.ToArray(), 1)
            ]
            : [new AcceptedResearchReportClaim(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "A cautious positive conclusion is supported by the finding.", evidenceIds.ToArray(), 0)];

        return new ResearchSynthesisResult(runId, ResearchReportStatus.Completed, null, "Executive summary.", "Evidence summary.", "Conflict summary.", "Limitations summary.", "Conclusion.", SynthesisConfidence.Limited, "FakeLLM", "fake-model", ResearchSynthesisPrompt.Version, DateTimeOffset.UtcNow, statistics, coverage, ["Abstract-level evidence only."], claims);
    }

    private static ResearchSynthesisResult CreateInsufficientResult(Guid runId)
    {
        var statistics = new SynthesisCorpusStatistics(1, 0, 0, 0, 0, 0, 1, 1, 0);
        var coverage = new SynthesisSourceCoverage(["PubMed"], true, false, false, false, 1);
        return new ResearchSynthesisResult(runId, ResearchReportStatus.InsufficientEvidence, ResearchReportInsufficientEvidenceReason.NoValidatedEvidence, "No evidence.", "No validated evidence.", "No conflicts assessed.", "Abstract-level evidence only.", "No conclusion.", SynthesisConfidence.InsufficientEvidence, null, null, ResearchSynthesisPrompt.Version, DateTimeOffset.UtcNow, statistics, coverage, ["No validated evidence."], []);
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

    private sealed record SeededRun(Guid RunId, Guid StudyId, Guid[] EvidenceIds, string? Pmid, string? Doi);
}
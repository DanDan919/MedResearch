using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Provenance;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Processing;
using MedResearch.Application.Research;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research;
using MedResearch.Infrastructure.Synthesis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchReportStoreTests
{
    [SkippableFact]
    public async Task SynthesisHotfix_TenStudies29Evidence10InsufficientEvaluations_PersistsFencedReport()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedTenStudyRunAsync();
        await using (var db = _fixture.CreateDbContext())
        {
            var run = await db.ResearchRuns.SingleAsync(x => x.Id == seed);
            var now = DateTimeOffset.UtcNow;
            run.StartPlanning(now);
            run.StartSearching(now);
            run.StartExtraction(now);
            run.StartEvaluation(now);
            run.StartSynthesis(now);
            run.AssignLease("sci-hf01", now, now.AddMinutes(5), 1);
            await db.SaveChangesAsync();
            var fence = new PostgreSqlResearchRunWriteFence(db);
            fence.Attach(new ClaimedResearchRun(run, "Does sleep improve recall?", "sci-hf01", 1, now.AddMinutes(5), false));
            var store = new EfResearchSynthesisStore(db, fence);
            var corpus = await new SynthesisContextBuilder(store, new(), NullLogger<SynthesisContextBuilder>.Instance).BuildAsync(seed, CancellationToken.None);
            Assert.Equal(10, corpus.Studies.Count);
            Assert.Equal(29, corpus.Statistics.IncludedEvidenceFindingCount);
            Assert.Equal(10, corpus.Statistics.EvaluatedStudyCount);
            Assert.All(corpus.Studies, study => Assert.Equal(MethodologicalConfidence.InsufficientInformation, study.Evaluation!.OverallConfidence));
            var draft = new ResearchReportDraft("Completed", null, null, null, null, null, null, "VeryLimited",
                corpus.Studies.SelectMany(study => study.Evidence).Take(12).Select((item, index) =>
                    new ResearchReportClaimDraft(index == 0 ? "Conclusion" : "Finding", "Positive", null, [item.EvidenceId.ToString()],
                        Kind: "QualitativeEffect", Outcome: item.Outcome, Population: item.Population,
                        ExposureOrIntervention: item.ExposureOrIntervention, Comparator: item.Comparator, Timepoint: item.Timepoint)).ToArray());
            var fake = new HotfixSynthesisClient(draft);
            var result = await new ResearchSynthesizer(fake, new ResearchReportDraftValidator(new()), NullLogger<ResearchSynthesizer>.Instance)
                .SynthesizeAsync(corpus, CancellationToken.None);
            await store.PersistReportAsync(result, CancellationToken.None);
            await store.PersistReportAsync(result, CancellationToken.None);
            Assert.Equal(1, fake.CallCount);
        }
        await using var fresh = _fixture.CreateDbContext();
        var report = await new EfResearchSynthesisStore(fresh).FindReportAsync(seed, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        Assert.Equal(12, report!.Claims.Count);
        Assert.All(report.Claims, claim => Assert.Equal(ResearchClaimGroundingStatus.StructuredValidated, claim.GroundingStatus));
        Assert.Equal(29, await fresh.Evidence.CountAsync(x => x.ResearchRunId == seed));
        Assert.Equal(10, await fresh.EvidenceExtractions.CountAsync(x => x.ResearchRunId == seed));
        Assert.Equal(10, await fresh.EvidenceEvaluations.CountAsync(x => x.ResearchRunId == seed && x.OverallConfidence == MethodologicalConfidence.InsufficientInformation));
        Assert.Equal(1, await fresh.ResearchReports.CountAsync(x => x.ResearchRunId == seed));
    }

    [SkippableFact]
    public async Task SynthesisHotfix_FailedRunRetainsPartialEvidenceAndOwnerIsolation()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedTenStudyRunAsync();
        await using (var db = _fixture.CreateDbContext())
        {
            var run = await db.ResearchRuns.SingleAsync(x => x.Id == seed);
            run.Fail(ResearchProcessingFailures.ReportClaimLimitMessage, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        await using var fresh = _fixture.CreateDbContext();
        var store = new EfResearchProvenanceStore(fresh);
        var partial = await store.FindAsync(seed, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        Assert.Equal(29, partial!.Studies.Sum(study => study.Evidence.Count));
        Assert.Equal(10, partial.Studies.Sum(study => study.Evaluations.Count));
        Assert.Empty(partial.ReportClaims);
        Assert.Null(await store.FindAsync(seed, "another-owner", CancellationToken.None));
        var progress = new EfResearchProgressStore(fresh);
        Assert.Null(await progress.FindResearchRunProgressSnapshotAsync(seed, "another-owner", CancellationToken.None));
        Assert.Equal(29, (await progress.FindResearchRunProgressSnapshotAsync(seed, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None))!.Metrics.EvidenceFindingCount);
        Assert.Null(await new EfResearchSynthesisStore(fresh).FindReportAsync(seed, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task SynthesisHotfix_PreviousStructuredReportRemainsAnIdempotencyBarrier()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        await using var db = _fixture.CreateDbContext();
        await new EfResearchSynthesisStore(db).PersistReportAsync(await CreateStructuredResultAsync(db, seed.RunId), CancellationToken.None);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_reports SET prompt_version = {ResearchSynthesisPrompt.PreviousStructuredVersion} WHERE research_run_id = {seed.RunId}");
        await using var fresh = _fixture.CreateDbContext();
        Assert.True(await new EfResearchSynthesisStore(fresh).HasReportAsync(seed.RunId, ResearchSynthesisPrompt.Version, CancellationToken.None));
        Assert.Equal(ResearchSynthesisPrompt.PreviousStructuredVersion,
            (await new EfResearchSynthesisStore(fresh).FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None))!.PromptVersion);
    }

    private async Task<Guid> SeedTenStudyRunAsync()
    {
        var seed = await SeedRunWithEvidenceAsync(2, discoveredStudyCount: 10);
        await using var db = _fixture.CreateDbContext();
        var searchId = await db.LiteratureSearches.Where(x => x.ResearchRunId == seed.RunId).Select(x => x.Id).SingleAsync();
        for (var index = 0; index < 9; index++)
        {
            var study = new Study(Guid.NewGuid(), $"Synthetic hotfix study {index}", "A trial reported improved recall in 120 adults.", $"10.4242/{Guid.NewGuid():N}", null, "Journal", null, "PubMed");
            var source = SourceMaterial.Create(study.Id, SourceMaterialType.Abstract, "PubMed", study.Doi, "SearchMetadataAbstract", study.Abstract!, 1,
                DateTimeOffset.UtcNow, null, null, null, SourceMaterialAccessStatus.Unknown, false, ["Abstract"]);
            var extraction = new EvidenceExtraction(Guid.NewGuid(), seed.RunId, study.Id, source.Id, EvidenceExtractionStatus.Completed, null,
                EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, 3, true);
            var findings = Enumerable.Range(0, 3).Select(_ => new Evidence(Guid.NewGuid(), seed.RunId, study.Id, extraction.Id, "recall", "Recall improved after sleep.",
                "reported improved recall in 120 adults", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true,
                "adults", "sleep", "wakefulness", "controlled trial", 120, null, null, null, null, null)).ToArray();
            db.Studies.Add(study);
            db.ResearchStudyDiscoveries.Add(new ResearchStudyDiscovery(Guid.NewGuid(), seed.RunId, searchId, study.Id, "PubMed", study.Doi, DateTimeOffset.UtcNow));
            db.SourceMaterials.Add(source);
            db.EvidenceExtractions.Add(extraction);
            db.Evidence.AddRange(findings);
            db.EvidenceEvaluations.Add(CreateEvaluation(seed.RunId, study.Id, findings.Select(x => x.Id).ToArray()));
        }
        await db.SaveChangesAsync();
        return seed.RunId;
    }

    private sealed class HotfixSynthesisClient(ResearchReportDraft draft) : IStructuredLlmClient
    {
        public int CallCount { get; private set; }
        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(typeof(ResearchReportDraft), typeof(T));
            using var schema = System.Text.Json.JsonDocument.Parse(request.OutputSchema.JsonSchema);
            Assert.Equal(12, schema.RootElement.GetProperty("properties").GetProperty("claims").GetProperty("maxItems").GetInt32());
            CallCount++;
            return Task.FromResult(new StructuredGenerationResult<T>((T)(object)draft, new("FakeLLM", "deterministic", null, DateTimeOffset.UtcNow)));
        }
    }

    private const string LegacyPromptVersion = "research-synthesizer-v2-trusted-evidence";
    private readonly PostgreSqlFixture _fixture;

    public ResearchReportStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ProvenanceProjection_IsRunScopedAndPreservesThePersistedLineageGraph()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedRunWithEvidenceAsync(evidenceCount: 2);
        await using (var context = _fixture.CreateDbContext())
        {
            var synthesisStore = new EfResearchSynthesisStore(context);
            await synthesisStore.PersistReportAsync(
                CreateCompletedResult(first.RunId, first.EvidenceIds),
                CancellationToken.None);
        }

        var second = await SeedSecondRunForExistingStudyAsync(first.StudyId);

        await using var verification = _fixture.CreateDbContext();
        var provenanceStore = new EfResearchProvenanceStore(verification);
        var firstModel = await provenanceStore.FindAsync(
            first.RunId,
            ResearchOwnership.LegacyUnownedSubjectId,
            CancellationToken.None);
        var secondModel = await provenanceStore.FindAsync(
            second.RunId,
            ResearchOwnership.LegacyUnownedSubjectId,
            CancellationToken.None);

        var firstStudy = Assert.Single(firstModel!.Studies);
        Assert.Equal(first.StudyId, firstStudy.StudyId);
        Assert.Single(firstStudy.DiscoveryPaths);
        Assert.Single(firstStudy.Extractions);
        Assert.Equal(2, firstStudy.Evidence.Count);
        Assert.Single(firstStudy.Evaluations);
        Assert.NotEmpty(firstModel.ReportClaims);
        Assert.Equal(
            first.EvidenceIds.OrderBy(id => id),
            firstModel.ReportClaims.SelectMany(claim => claim.EvidenceIds).Distinct().OrderBy(id => id));
        Assert.DoesNotContain(firstStudy.SourceMaterials, material => material.ContentHash is null);

        var secondStudy = Assert.Single(secondModel!.Studies);
        Assert.Equal(first.StudyId, secondStudy.StudyId);
        Assert.Single(secondStudy.Extractions);
        Assert.Single(secondStudy.Evidence);
        Assert.All(first.EvidenceIds, id => Assert.DoesNotContain(id, secondStudy.Evidence.Select(item => item.EvidenceId)));
        Assert.Empty(secondModel.ReportClaims);
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
        Assert.Equal(LegacyPromptVersion, report.PromptVersion);
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
        var report = await readStore.FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(seed.RunId, report.ResearchRunId);
        var claim = Assert.Single(report.Claims);
        var citation = Assert.Single(claim.Citations);
        Assert.Equal(seed.EvidenceIds[0], citation.EvidenceId);
        Assert.Equal(seed.StudyId, citation.StudyId);
        Assert.Equal(seed.Pmid, citation.Pmid);
        Assert.Equal(seed.Doi, citation.Doi);
        Assert.Equal("Sleep and recall report", citation.Title);
        Assert.Equal("Journal", citation.Journal);
        Assert.Equal(2026, citation.PublicationYear);
        Assert.Equal("PubMed", citation.StudySource);
        Assert.Equal("recall", citation.Outcome);
        Assert.Equal("Recall improved after sleep.", citation.ResultSummary);
        Assert.Equal("reported improved recall in 120 adults", citation.SupportingText);
        Assert.Equal(EvidenceSourceScope.Abstract, citation.SourceScope);
        Assert.True(citation.GroundingValidated);
        Assert.Equal("adults", citation.Population);
        Assert.Equal("sleep", citation.ExposureOrIntervention);
        Assert.Equal("wakefulness", citation.Comparator);
        Assert.Equal("controlled trial", citation.StudyDesign);
        Assert.Equal(120, citation.SampleSize);
        Assert.NotNull(citation.SourceMaterial);
        Assert.Equal(SourceMaterialType.Abstract.ToString(), citation.SourceMaterial.Type);
        Assert.Equal("PubMed", citation.SourceMaterial.Provider);
        Assert.Equal("SearchMetadataAbstract", citation.SourceMaterial.RetrievalMethod);
        Assert.Equal(1, citation.SourceMaterial.ContentVersion);
        Assert.Equal(SourceMaterialAccessStatus.Unknown.ToString(), citation.SourceMaterial.AccessStatus);
        Assert.False(citation.SourceMaterial.WasTruncated);
    }

    [SkippableFact]
    public async Task EvidenceCorpus_ReloadsFullClaimToSourceMaterialLineage()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await store.PersistReportAsync(CreateCompletedResult(seed.RunId, seed.EvidenceIds), CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var trace = await (
            from report in verification.ResearchReports.AsNoTracking()
            join claim in verification.ResearchReportClaims.AsNoTracking()
                on report.Id equals claim.ResearchReportId
            join link in verification.ResearchReportClaimEvidence.AsNoTracking()
                on claim.Id equals link.ResearchReportClaimId
            join evidence in verification.Evidence.AsNoTracking()
                on link.EvidenceId equals evidence.Id
            join extraction in verification.EvidenceExtractions.AsNoTracking()
                on evidence.EvidenceExtractionId equals extraction.Id
            join source in verification.SourceMaterials.AsNoTracking()
                on extraction.SourceMaterialId equals source.Id
            join study in verification.Studies.AsNoTracking()
                on evidence.StudyId equals study.Id
            where report.ResearchRunId == seed.RunId
            select new { report, claim, evidence, extraction, source, study })
            .SingleAsync();

        Assert.Equal(seed.RunId, trace.report.ResearchRunId);
        Assert.Equal(seed.EvidenceIds[0], trace.evidence.Id);
        Assert.Equal(trace.evidence.EvidenceExtractionId, trace.extraction.Id);
        Assert.Equal(trace.extraction.SourceMaterialId, trace.source.Id);
        Assert.Equal(trace.evidence.StudyId, trace.source.StudyId);
        Assert.Equal(trace.evidence.StudyId, trace.study.Id);
        Assert.Equal(SourceMaterial.ComputeContentHash("A trial reported improved recall in 120 adults."), trace.source.ContentHash);
        Assert.Equal(1, trace.source.ContentVersion);
        Assert.Equal(seed.Pmid, trace.study.Pmid);
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

    [SkippableFact]
    public async Task LoadCorpusAsync_MultipleDiscoveryPathsPreserveSearchProvenanceButDeduplicateStudySnapshot()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        await using (var context = _fixture.CreateDbContext())
        {
            var secondSearch = new LiteratureSearch(Guid.NewGuid(), seed.RunId, "PubMed", "sleep memory", DateTimeOffset.UtcNow.AddSeconds(1), 1, 0, 1);
            var secondDiscovery = new ResearchStudyDiscovery(Guid.NewGuid(), seed.RunId, secondSearch.Id, seed.StudyId, "PubMed", seed.Pmid, DateTimeOffset.UtcNow.AddSeconds(1));
            context.LiteratureSearches.Add(secondSearch);
            context.ResearchStudyDiscoveries.Add(secondDiscovery);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var store = new EfResearchSynthesisStore(verification);
        var corpus = await store.LoadCorpusAsync(seed.RunId, CancellationToken.None);

        var study = Assert.Single(corpus.Studies);
        Assert.Equal(seed.StudyId, study.StudyId);
        Assert.Equal(2, corpus.Searches.Count);
        Assert.Equal(seed.EvidenceIds[0], Assert.Single(corpus.Evidence).EvidenceId);
    }

    [SkippableFact]
    public async Task ReportCitationGraph_UsesRunScopedEvidenceAndAuthoritativeSharedStudyMetadata()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        var second = await SeedSecondRunForExistingStudyAsync(first.StudyId);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await store.PersistReportAsync(CreateCompletedResult(second.RunId, second.EvidenceIds), CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var citationRows = await (
            from report in verification.ResearchReports.AsNoTracking()
            join claim in verification.ResearchReportClaims.AsNoTracking()
                on report.Id equals claim.ResearchReportId
            join link in verification.ResearchReportClaimEvidence.AsNoTracking()
                on claim.Id equals link.ResearchReportClaimId
            join evidence in verification.Evidence.AsNoTracking()
                on link.EvidenceId equals evidence.Id
            join study in verification.Studies.AsNoTracking()
                on evidence.StudyId equals study.Id
            where report.ResearchRunId == second.RunId
            orderby claim.Ordinal, link.Ordinal
            select new
            {
                ReportRunId = report.ResearchRunId,
                EvidenceRunId = evidence.ResearchRunId,
                EvidenceId = evidence.Id,
                StudyId = study.Id,
                study.Pmid,
                study.Doi,
                study.Title,
                evidence.SupportingText
            }).ToArrayAsync();

        var row = Assert.Single(citationRows);
        Assert.Equal(second.RunId, row.ReportRunId);
        Assert.Equal(second.RunId, row.EvidenceRunId);
        Assert.Equal(second.EvidenceIds[0], row.EvidenceId);
        Assert.Equal(first.StudyId, row.StudyId);
        Assert.Equal(first.Pmid, row.Pmid);
        Assert.Equal(first.Doi, row.Doi);
        Assert.Equal("Sleep and recall report", row.Title);
        Assert.Equal("reported improved recall in 120 adults", row.SupportingText);
    }

    [SkippableFact]
    public async Task FindReportAsync_DoesNotProjectCrossRunClaimEvidenceLink()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        var second = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await store.PersistReportAsync(CreateCompletedResult(second.RunId, second.EvidenceIds), CancellationToken.None);

            var report = await context.ResearchReports.SingleAsync(report => report.ResearchRunId == second.RunId, CancellationToken.None);
            var claim = await context.ResearchReportClaims.SingleAsync(claim => claim.ResearchReportId == report.Id, CancellationToken.None);
            context.ResearchReportClaimEvidence.Add(new ResearchReportClaimEvidence(claim.Id, first.EvidenceIds[0], 9));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var readStore = new EfResearchSynthesisStore(verification);
        var reportReadModel = await readStore.FindReportAsync(second.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);

        Assert.NotNull(reportReadModel);
        var claimReadModel = Assert.Single(reportReadModel.Claims);
        var citation = Assert.Single(claimReadModel.Citations);
        Assert.Equal(second.EvidenceIds[0], citation.EvidenceId);
        Assert.DoesNotContain(claimReadModel.Citations, item => item.EvidenceId == first.EvidenceIds[0]);
    }

    [SkippableFact]
    public async Task PersistReportAsync_RejectsCrossRunEvidenceCitationBeforeWritingReport()
    {
        SkipIfPostgreSqlUnavailable();

        var first = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        var second = await SeedRunWithEvidenceAsync(evidenceCount: 1);
        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchSynthesisStore(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.PersistReportAsync(
                CreateCompletedResult(second.RunId, first.EvidenceIds),
                CancellationToken.None));
        }

        await using var verification = _fixture.CreateDbContext();
        Assert.Empty(await verification.ResearchReports.Where(report => report.ResearchRunId == second.RunId).ToArrayAsync());
    }

    [SkippableFact]
    public async Task StructuredReport_RoundtripsThroughFreshContextAndProvenance_Idempotently()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        await using (var db = _fixture.CreateDbContext())
        {
            var result = await CreateStructuredResultAsync(db, seed.RunId);
            var store = new EfResearchSynthesisStore(db);
            await store.PersistReportAsync(result, CancellationToken.None);
            await store.PersistReportAsync(result, CancellationToken.None);
        }
        await using var read = _fixture.CreateDbContext();
        var report = await new EfResearchSynthesisStore(read).FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        var claim = Assert.Single(report!.Claims);
        Assert.Equal(ResearchClaimGroundingStatus.StructuredValidated, claim.GroundingStatus);
        Assert.Equal("recall", claim.Semantics!.Outcome);
        Assert.Null(claim.Semantics.Timepoint);
        Assert.Equal(seed.EvidenceIds, claim.Semantics.EvidenceIds);
        Assert.Equal(seed.EvidenceIds[0], Assert.Single(claim.Citations).EvidenceId);
        var provenance = await new EfResearchProvenanceStore(read).FindAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(claim.Semantics), System.Text.Json.JsonSerializer.Serialize(Assert.Single(provenance!.ReportClaims).Semantics));
        Assert.Single(await read.ResearchReports.Where(x => x.ResearchRunId == seed.RunId).ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("text")]
    [InlineData("scope")]
    [InlineData("sections")]
    [InlineData("unstructured")]
    public async Task StructuredPersistence_RevalidatesAcceptedClaimsAndRejectsBypass(string attack)
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        await using (var db = _fixture.CreateDbContext())
        {
            var result = await CreateStructuredResultAsync(db, seed.RunId);
            var claim = Assert.Single(result.Claims);
            result = attack switch
            {
                "text" => result with { Claims = [claim with { Text = "Treatment cures dementia." }] },
                "scope" => result with { Claims = [claim with { Semantics = claim.Semantics! with { Population = "all patients" } }] },
                "sections" => result with { Conclusion = "Treatment cures dementia." },
                _ => result with { Claims = [claim with { Semantics = null }] }
            };
            if (attack == "scope")
                await Assert.ThrowsAsync<ResearchSynthesisValidationException>(() => new EfResearchSynthesisStore(db).PersistReportAsync(result, CancellationToken.None));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => new EfResearchSynthesisStore(db).PersistReportAsync(result, CancellationToken.None));
        }
        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.ResearchReports.AnyAsync(x => x.ResearchRunId == seed.RunId));
    }

    [SkippableFact]
    public async Task StructuredRead_FailsClosedOnChangedAuthoritativeText()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        await using (var db = _fixture.CreateDbContext())
        {
            await new EfResearchSynthesisStore(db).PersistReportAsync(await CreateStructuredResultAsync(db, seed.RunId), CancellationToken.None);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE research_report_claims SET text = 'Unsupported benefit' WHERE research_report_id IN (SELECT id FROM research_reports WHERE research_run_id = {seed.RunId})");
        }
        await using var read = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfResearchSynthesisStore(read).FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfResearchProvenanceStore(read).FindAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task LegacyClaims_RemainExplicitlyUnverifiedAfterMigration()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        await using var db = _fixture.CreateDbContext();
        await new EfResearchSynthesisStore(db).PersistReportAsync(CreateCompletedResult(seed.RunId, seed.EvidenceIds), CancellationToken.None);
        db.ChangeTracker.Clear();
        var report = await new EfResearchSynthesisStore(db).FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        Assert.Equal(ResearchClaimGroundingStatus.LegacyUnverified, Assert.Single(report!.Claims).GroundingStatus);
        Assert.Null(Assert.Single(report.Claims).Semantics);
    }

    [SkippableFact]
    public async Task StructuredEmptyCorpus_CompletesWithoutInventedClaimOrModelProse()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(0);
        await using var db = _fixture.CreateDbContext();
        var context = await new SynthesisContextBuilder(new EfResearchSynthesisStore(db), new(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SynthesisContextBuilder>.Instance).BuildAsync(seed.RunId, CancellationToken.None);
        var result = new ResearchReportDraftValidator(new()).CreateInsufficientEvidenceResult(context);
        await new EfResearchSynthesisStore(db).PersistReportAsync(result, CancellationToken.None);
        var report = await new EfResearchSynthesisStore(db).FindReportAsync(seed.RunId, ResearchOwnership.LegacyUnownedSubjectId, CancellationToken.None);
        Assert.Equal(ResearchReportStatus.InsufficientEvidence, report!.Status);
        Assert.Empty(report.Claims);
        Assert.Contains("absence of evidence is not no effect", report.Conclusion);
    }

    [SkippableFact]
    public async Task StructuredReport_StaleWorkerCannotPersistAfterLeaseTransfer()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedRunWithEvidenceAsync(1);
        var now = DateTimeOffset.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            var run = await db.ResearchRuns.SingleAsync(x => x.Id == seed.RunId);
            run.StartPlanning(now);
            run.AssignLease("f16-worker-a", now, now.AddMinutes(5), 1);
            await db.SaveChangesAsync();
        }
        await using var stale = _fixture.CreateDbContext();
        var result = await CreateStructuredResultAsync(stale, seed.RunId);
        var snapshot = await stale.ResearchRuns.AsNoTracking().SingleAsync(x => x.Id == seed.RunId);
        var fence = new MedResearch.Infrastructure.Research.Processing.PostgreSqlResearchRunWriteFence(stale);
        fence.Attach(new MedResearch.Application.Research.Processing.ClaimedResearchRun(snapshot, "question", "f16-worker-a", 1, now.AddMinutes(5), false));
        await using (var newer = _fixture.CreateDbContext())
        {
            var run = await newer.ResearchRuns.SingleAsync(x => x.Id == seed.RunId);
            run.AssignLease("f16-worker-b", now, now.AddMinutes(5), 2);
            await newer.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<MedResearch.Application.Research.Processing.ResearchRunLeaseLostException>(() => new EfResearchSynthesisStore(stale, fence).PersistReportAsync(result, CancellationToken.None));
        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.ResearchReports.AnyAsync(x => x.ResearchRunId == seed.RunId));
    }

    private static async Task<ResearchSynthesisResult> CreateStructuredResultAsync(MedResearch.Infrastructure.Persistence.MedResearchDbContext db, Guid runId)
    {
        var context = await new SynthesisContextBuilder(new EfResearchSynthesisStore(db), new(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SynthesisContextBuilder>.Instance).BuildAsync(runId, CancellationToken.None);
        var item = Assert.Single(context.Studies.SelectMany(x => x.Evidence));
        var proposal = new ResearchReportClaimDraft("Conclusion", "Positive", null, [item.EvidenceId.ToString()], Kind: "QualitativeEffect", Outcome: item.Outcome,
            Population: item.Population, ExposureOrIntervention: item.ExposureOrIntervention, Comparator: item.Comparator, Timepoint: item.Timepoint);
        return new ResearchReportDraftValidator(new()).Validate(context, new("Completed", null, null, null, null, null, null, "Limited", [proposal]), "fake", "fake", DateTimeOffset.UtcNow);
    }

    private async Task<SeededRun> SeedRunWithEvidenceAsync(int evidenceCount, int discoveredStudyCount = 1)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does sleep improve recall?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var plan = new ResearchPlan(Guid.NewGuid(), run.Id, question.Id, question.Text, "adults", "sleep", "wakefulness", ["recall"], ["controlled trial"], ["sleep recall"], [], "FakeLLM", "fake-model", "research-planner-v1", DateTimeOffset.UtcNow);
        var search = new LiteratureSearch(Guid.NewGuid(), run.Id, "PubMed", "sleep recall", DateTimeOffset.UtcNow, discoveredStudyCount, discoveredStudyCount, 0, plan.Id);
        var doi = $"10.4242/{Guid.NewGuid():N}";
        var pmid = RandomPmid();
        var study = new Study(Guid.NewGuid(), "Sleep and recall report", "A trial reported improved recall in 120 adults.", doi, pmid, "Journal", new DateOnly(2026, 1, 1), "PubMed");
        var discovery = new ResearchStudyDiscovery(Guid.NewGuid(), run.Id, search.Id, study.Id, "PubMed", study.Pmid, DateTimeOffset.UtcNow);
        var sourceMaterial = SourceMaterial.Create(study.Id, SourceMaterialType.Abstract, "PubMed", study.Pmid, "SearchMetadataAbstract", study.Abstract!, 1, DateTimeOffset.UtcNow, null, null, null, SourceMaterialAccessStatus.Unknown, false, ["Abstract"]);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, study.Id, sourceMaterial.Id, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, evidenceCount, true);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.ResearchPlans.Add(plan);
        context.LiteratureSearches.Add(search);
        context.Studies.Add(study);
        context.ResearchStudyDiscoveries.Add(discovery);
        context.SourceMaterials.Add(sourceMaterial);
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
        var sourceMaterialId = await context.SourceMaterials.Where(sourceMaterial => sourceMaterial.StudyId == studyId && sourceMaterial.IsCurrent).Select(sourceMaterial => sourceMaterial.Id).FirstAsync(CancellationToken.None);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, studyId, sourceMaterialId, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", EvidenceExtractionPrompt.Version, DateTimeOffset.UtcNow, 1, true);
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
        var statistics = new SynthesisCorpusStatistics(1, 1, 1, evidenceIds.Count, 1, evidenceIds.Count, 1, 0, 1, 0, 1, 0);
        var coverage = new SynthesisSourceCoverage(["PubMed"], false, false, false, false, 1);
        AcceptedResearchReportClaim[] claims = evidenceIds.Count > 1
            ? [
                new AcceptedResearchReportClaim(ResearchReportClaimType.Finding, ResearchReportClaimDirection.Positive, "Recall improved in the cited evidence.", [evidenceIds.First()], 0),
                new AcceptedResearchReportClaim(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "A cautious positive conclusion is supported by both findings.", evidenceIds.ToArray(), 1)
            ]
            : [new AcceptedResearchReportClaim(ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "A cautious positive conclusion is supported by the finding.", evidenceIds.ToArray(), 0)];

        return new ResearchSynthesisResult(runId, ResearchReportStatus.Completed, null, "Executive summary.", "Evidence summary.", "Conflict summary.", "Limitations summary.", "Conclusion.", SynthesisConfidence.Limited, "FakeLLM", "fake-model", LegacyPromptVersion, DateTimeOffset.UtcNow, statistics, coverage, ["Abstract-level evidence only."], claims);
    }

    private static ResearchSynthesisResult CreateInsufficientResult(Guid runId)
    {
        var statistics = new SynthesisCorpusStatistics(1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 1);
        var coverage = new SynthesisSourceCoverage(["PubMed"], true, false, false, false, 1);
        return new ResearchSynthesisResult(runId, ResearchReportStatus.InsufficientEvidence, ResearchReportInsufficientEvidenceReason.NoValidatedEvidence, "No evidence.", "No validated evidence.", "No conflicts assessed.", "Abstract-level evidence only.", "No conclusion.", SynthesisConfidence.InsufficientEvidence, null, null, LegacyPromptVersion, DateTimeOffset.UtcNow, statistics, coverage, ["No validated evidence."], []);
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

using MedResearch.Application.Research.Extraction;
using MedResearch.Domain;
using MedResearch.Infrastructure.Extraction.Persistence;
using Microsoft.EntityFrameworkCore;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Infrastructure.Synthesis.Persistence;

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
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [CreateFinding("recall", "Recall improved after sleep in 120 adults.")]);

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
    public async Task PersistExtractionResultAsync_RoundTripsSourceAnchoredNumericGrounding()
    {
        SkipIfPostgreSqlUnavailable();

        const string source = "The odds ratio was 0.73 (95% CI 0.55 to 0.96, p = 0.03).";
        var seed = await SeedDiscoveredStudyAsync("Does the treatment affect the odds ratio?", source);
        var anchor = new SourceAnchorResolver().Resolve(seed.SourceMaterialId!.Value, source, source).Anchor!;
        var facts = new[]
        {
            new NumericGroundingFact(NumericGroundingField.EffectMeasure, NumericGroundingStatus.Verified, anchor, null),
            new NumericGroundingFact(NumericGroundingField.EffectEstimate, NumericGroundingStatus.Verified, anchor, null),
            new NumericGroundingFact(NumericGroundingField.ConfidenceInterval, NumericGroundingStatus.Verified, anchor, null),
            new NumericGroundingFact(NumericGroundingField.PValue, NumericGroundingStatus.Verified, anchor, null)
        };
        var finding = new AcceptedEvidenceFinding(
            "odds ratio",
            source,
            source,
            EvidenceDirection.Positive,
            null,
            null,
            null,
            "randomized controlled trial",
            null,
            "OR",
            0.73m,
            0.55m,
            0.96m,
            0.03m,
            0.95m,
            null,
            "=",
            facts);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            await store.PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId.Value, [finding]), CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        var evidence = await verification.Evidence.SingleAsync(item => item.ResearchRunId == seed.RunId);
        Assert.Equal("=", evidence.PValueOperator);
        var effectFact = Assert.Single(evidence.NumericGrounding, fact => fact.Field == NumericGroundingField.EffectEstimate);
        Assert.Equal(NumericGroundingStatus.Verified, effectFact.Status);
        Assert.Equal(seed.SourceMaterialId, effectFact.Anchor?.SourceMaterialId);
        Assert.Equal(anchor.SpanHash, effectFact.Anchor?.SpanHash);
        Assert.Equal(anchor.StartOffset, effectFact.Anchor?.StartOffset);
        Assert.Equal(anchor.LexicalText, effectFact.Anchor?.LexicalText);
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_FreshReadRechecksTupleProofAndTimepoint()
    {
        SkipIfPostgreSqlUnavailable();
        const string source = "In adults, drug A versus placebo at 12 weeks: Mortality OR 0.73 (95% CI 0.55 to 0.96).";
        var seed = await SeedDiscoveredStudyAsync("Does drug A affect mortality?", source);
        var studyContext = new EvidenceExtractionStudyContext(seed.RunId, Guid.NewGuid(), "Question", null, seed.StudyId, seed.SourceMaterialId,
            EvidenceSourceScope.Abstract, "Fixture", source, SourceMaterial.ComputeContentHash(source), false, ["Abstract"], "Study", source,
            null, null, null, null, null, [], [], "Fixture");
        var draft = new EvidenceFindingDraft("Mortality", "Mortality OR 0.73.", source, "Positive", "adults", "drug A", "placebo",
            "randomized controlled trial", null, "OR", 0.73m, 0.55m, 0.96m, null, 0.95m, Timepoint: "12 weeks");
        var finding = Assert.Single(new EvidenceExtractionDraftValidator().Validate(studyContext, new([draft])));
        // Simulate a corrupted accepted snapshot: status/hash alone must not be authority.
        finding = finding with { ConfidenceIntervalLower = 0.1234m, ConfidenceIntervalUpper = 0.2345m, ResultSummary = "CI 0.1234 to 0.2345." };
        await using (var db = _fixture.CreateDbContext())
        {
            await new EfEvidenceExtractionStore(db).PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [finding]), CancellationToken.None);
        }
        await using var fresh = _fixture.CreateDbContext();
        var persisted = await fresh.Evidence.SingleAsync(item => item.ResearchRunId == seed.RunId);
        Assert.Equal("12 weeks", persisted.Timepoint);
        Assert.Equal(NumericGroundingStatus.Verified, Assert.Single(persisted.NumericGrounding, fact => fact.Field == NumericGroundingField.Timepoint).Status);
        var corpus = await new EvidenceCorpusBuilder(new EfResearchSynthesisStore(fresh)).BuildAsync(seed.RunId, CancellationToken.None);
        Assert.Equal(0, new QuantitativeEvidenceAssessor().Assess(corpus).EligibleEvidenceCount);
        var read = Assert.Single(corpus.Evidence);
        Assert.Equal(0.73m, read.EffectValue);
        Assert.Equal(NumericGroundingStatus.Unsupported, Assert.Single(read.NumericGrounding!, fact => fact.Field == NumericGroundingField.ConfidenceInterval).Status);
        Assert.DoesNotContain("0.1234", read.ResultSummary);
        var synthesisContext = await new SynthesisContextBuilder(new EfResearchSynthesisStore(fresh), new SynthesisOptions(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SynthesisContextBuilder>.Instance).BuildAsync(seed.RunId, CancellationToken.None);
        Assert.Null(Assert.Single(Assert.Single(synthesisContext.Studies).Evidence).ConfidenceIntervalLower);
        Assert.DoesNotContain("0.1234", ResearchSynthesisPrompt.Create(synthesisContext).UserPrompt);
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_AllowsMultipleFindingsForOneStudy()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does sleep affect memory outcomes?", "Recall improved after sleep. Attention did not clearly change.");
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [
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
        await store.PersistExtractionResultAsync(CreateCompletedResult(first.RunId, first.StudyId, first.SourceMaterialId!.Value, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);
        await store.PersistExtractionResultAsync(CreateCompletedResult(secondRunId, first.StudyId, first.SourceMaterialId!.Value, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);

        Assert.Equal(2, await context.EvidenceExtractions.CountAsync(extraction => extraction.StudyId == first.StudyId));
        Assert.Equal(2, await context.Evidence.CountAsync(evidence => evidence.StudyId == first.StudyId));
    }

    [SkippableFact]
    public async Task PersistExtractionResultAsync_IsIdempotentForSameRunStudyAndPromptVersion()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does sleep idempotency work?", "Recall improved after sleep.");
        var result = CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [CreateFinding("recall", "Recall improved after sleep.")]);

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
            Assert.Equal(seed.SourceMaterialId, study.SourceMaterialId);
            Assert.Equal("Does preserved context work?", study.ResearchQuestion);
            Assert.Equal("adults", study.Plan?.Population);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            await store.PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [CreateFinding("recall", "Recall improved after sleep.")]), CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            var workItems = await store.FindStudiesForExtractionAsync(seed.RunId, EvidenceExtractionPrompt.Version, 10, CancellationToken.None);

            Assert.Empty(workItems.Studies);
        }
    }

    [SkippableFact]
    public async Task FindStudiesForExtractionAsync_MultipleDiscoveryPathsYieldOneStudyWorkItem()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedDiscoveredStudyAsync("Does repeated discovery duplicate extraction work?", "Recall improved after sleep.");
        await using (var context = _fixture.CreateDbContext())
        {
            var secondSearch = new LiteratureSearch(Guid.NewGuid(), seed.RunId, "PubMed", "sleep memory", DateTimeOffset.UtcNow.AddSeconds(1), 1, 0, 1);
            var secondDiscovery = new ResearchStudyDiscovery(Guid.NewGuid(), seed.RunId, secondSearch.Id, seed.StudyId, "PubMed", RandomPmid(), DateTimeOffset.UtcNow.AddSeconds(1));
            context.LiteratureSearches.Add(secondSearch);
            context.ResearchStudyDiscoveries.Add(secondDiscovery);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfEvidenceExtractionStore(context);
            var workItems = await store.FindStudiesForExtractionAsync(seed.RunId, EvidenceExtractionPrompt.Version, 10, CancellationToken.None);

            Assert.Equal(1, workItems.TotalDiscoveredStudyCount);
            var study = Assert.Single(workItems.Studies);
            Assert.Equal(seed.StudyId, study.StudyId);
            Assert.Equal(seed.SourceMaterialId, study.SourceMaterialId);
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
        await store.PersistExtractionResultAsync(CreateCompletedResult(seed.RunId, seed.StudyId, seed.SourceMaterialId!.Value, [finding]), CancellationToken.None);

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
        var sourceMaterial = abstractText is null ? null : SourceMaterial.Create(
            study.Id,
            SourceMaterialType.Abstract,
            "PubMed",
            study.Pmid,
            "SearchMetadataAbstract",
            abstractText,
            1,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            SourceMaterialAccessStatus.Unknown,
            false,
            ["Abstract"]);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.ResearchPlans.Add(plan);
        context.LiteratureSearches.Add(search);
        context.Studies.Add(study);
        context.ResearchStudyDiscoveries.Add(discovery);
        if (sourceMaterial is not null)
        {
            context.SourceMaterials.Add(sourceMaterial);
        }
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededStudy(run.Id, study.Id, sourceMaterial?.Id);
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
        Guid sourceMaterialId,
        IReadOnlyCollection<AcceptedEvidenceFinding> findings)
    {
        return new EvidenceExtractionResult(
            runId,
            studyId,
            sourceMaterialId,
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

    private sealed record SeededStudy(Guid RunId, Guid StudyId, Guid? SourceMaterialId);
}

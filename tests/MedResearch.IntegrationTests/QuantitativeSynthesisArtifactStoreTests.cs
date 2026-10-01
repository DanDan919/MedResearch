using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Research.Processing;
using MedResearch.Infrastructure.Synthesis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class QuantitativeSynthesisArtifactStoreTests
{
    private readonly PostgreSqlFixture _fixture;

    public QuantitativeSynthesisArtifactStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PersistsExactSnapshotAndContributionLineageIdempotently()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedAsync();
        var contribution = new QuantitativeSynthesisContribution(
            seed.EvidenceId,
            seed.StudyId,
            seed.ExtractionId,
            seed.SourceMaterialId,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            2d,
            1d);
        var result = CreateResult(seed.RunId, contribution);
        var readiness = new QuantitativeSynthesisReadiness(seed.RunId, [result], 1, 0, "fixed-effect-inverse-variance-v1");

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfQuantitativeSynthesisArtifactStore(context);
            await store.PersistAsync(readiness, CancellationToken.None);
            await store.PersistAsync(readiness, CancellationToken.None);
        }

        await using var verification = _fixture.CreateDbContext();
        Assert.Equal(1, await verification.QuantitativeSynthesisArtifacts.CountAsync(artifact => artifact.ResearchRunId == seed.RunId));
        Assert.Equal(1, await verification.QuantitativeSynthesisContributionSnapshots.CountAsync(snapshot => snapshot.EvidenceId == seed.EvidenceId));
        var readModel = Assert.Single(await new EfQuantitativeSynthesisArtifactStore(verification).FindByResearchRunIdAsync(seed.RunId, CancellationToken.None));
        Assert.Equal(result.AnalysisScaleEffect, readModel.Result.AnalysisScaleEffect);
        Assert.Equal(result.Contributions.Single().EvidenceId, readModel.Result.Contributions.Single().EvidenceId);
        Assert.Equal(QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result), readModel.SnapshotFingerprint);
    }

    [SkippableFact]
    public async Task RejectsContributionFromAnotherResearchRun()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedAsync();
        var otherRunId = Guid.NewGuid();
        await using (var context = _fixture.CreateDbContext())
        {
            var question = new ResearchQuestion("Other question", DateTimeOffset.UtcNow);
            context.ResearchQuestions.Add(question);
            context.ResearchRuns.Add(new ResearchRun(
                otherRunId,
                question.Id,
                ResearchRunStatus.Queued,
                question.CreatedAt,
                null,
                null,
                null));
            await context.SaveChangesAsync();
        }

        var result = CreateResult(otherRunId, new QuantitativeSynthesisContribution(
            seed.EvidenceId,
            seed.StudyId,
            seed.ExtractionId,
            seed.SourceMaterialId,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            2d,
            1d));

        await using var storeContext = _fixture.CreateDbContext();
        var store = new EfQuantitativeSynthesisArtifactStore(storeContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PersistAsync(
            new QuantitativeSynthesisReadiness(otherRunId, [result], 1, 0, result.AlgorithmVersion),
            CancellationToken.None));
    }

    [SkippableFact]
    public async Task RejectsContributionWhoseSourceMaterialDiffersFromExtractionSnapshot()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedAsync();
        Guid otherSourceMaterialId;
        await using (var context = _fixture.CreateDbContext())
        {
            var other = SourceMaterial.Create(
                seed.StudyId,
                SourceMaterialType.StructuredFullText,
                "EuropePMC",
                "other-source",
                "fixture",
                "different source text",
                1,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                SourceMaterialAccessStatus.OpenAccess,
                false,
                []);
            context.SourceMaterials.Add(other);
            await context.SaveChangesAsync();
            otherSourceMaterialId = other.Id;
        }

        var result = CreateResult(seed.RunId, new QuantitativeSynthesisContribution(
            seed.EvidenceId,
            seed.StudyId,
            seed.ExtractionId,
            otherSourceMaterialId,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            2d,
            1d));

        await using var storeContext = _fixture.CreateDbContext();
        var store = new EfQuantitativeSynthesisArtifactStore(storeContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PersistAsync(
            new QuantitativeSynthesisReadiness(seed.RunId, [result], 1, 0, result.AlgorithmVersion),
            CancellationToken.None));
    }

    [SkippableFact]
    public async Task RejectsRelationalContributionDriftWhenReadingArtifact()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedAsync();
        var contribution = new QuantitativeSynthesisContribution(
            seed.EvidenceId,
            seed.StudyId,
            seed.ExtractionId,
            seed.SourceMaterialId,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            2d,
            1d);
        var result = CreateResult(seed.RunId, contribution);

        await using (var context = _fixture.CreateDbContext())
        {
            await new EfQuantitativeSynthesisArtifactStore(context).PersistAsync(
                new QuantitativeSynthesisReadiness(seed.RunId, [result], 1, 0, result.AlgorithmVersion),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var row = await context.QuantitativeSynthesisContributionSnapshots.SingleAsync();
            row.NormalizedWeight += 0.25d;
            await context.SaveChangesAsync();
        }

        await using var verification = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfQuantitativeSynthesisArtifactStore(verification)
            .FindByResearchRunIdAsync(seed.RunId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task StaleWorkerCannotPersistArtifactAfterLeaseOwnershipChanges()
    {
        SkipIfPostgreSqlUnavailable();
        var seed = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var context = _fixture.CreateDbContext())
        {
            var run = await context.ResearchRuns.SingleAsync(item => item.Id == seed.RunId);
            run.StartPlanning(now);
            run.AssignLease("worker-a", now, now.AddMinutes(5), 1);
            await context.SaveChangesAsync();
        }

        var contribution = new QuantitativeSynthesisContribution(
            seed.EvidenceId,
            seed.StudyId,
            seed.ExtractionId,
            seed.SourceMaterialId,
            0.25d,
            0.5d,
            Math.Sqrt(0.5d),
            2d,
            1d);
        var result = CreateResult(seed.RunId, contribution);
        var readiness = new QuantitativeSynthesisReadiness(seed.RunId, [result], 1, 0, result.AlgorithmVersion);

        await using var workerContext = _fixture.CreateDbContext();
        var runSnapshot = await workerContext.ResearchRuns.AsNoTracking().SingleAsync(item => item.Id == seed.RunId);
        var fence = new PostgreSqlResearchRunWriteFence(workerContext);
        fence.Attach(new ClaimedResearchRun(runSnapshot, "Does the intervention change the outcome?", "worker-a", 1, now.AddMinutes(5), false));
        var store = new EfQuantitativeSynthesisArtifactStore(workerContext, fence);

        await using (var takeoverContext = _fixture.CreateDbContext())
        {
            var run = await takeoverContext.ResearchRuns.SingleAsync(item => item.Id == seed.RunId);
            run.AssignLease("worker-b", now, now.AddMinutes(5), 2);
            await takeoverContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ResearchRunLeaseLostException>(() => store.PersistAsync(readiness, CancellationToken.None));
        await using var verification = _fixture.CreateDbContext();
        Assert.Empty(await verification.QuantitativeSynthesisArtifacts.Where(item => item.ResearchRunId == seed.RunId).ToArrayAsync());
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does the intervention change the outcome?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var studyId = Guid.NewGuid();
        var providerId = studyId.ToString("N");
        var study = new Study(studyId, "Study title", "Abstract", $"10.1000/example-{providerId}", providerId, "Journal", new DateOnly(2025, 1, 2), "PubMed");
        var material = SourceMaterial.Create(study.Id, SourceMaterialType.Abstract, "PubMed", providerId, "fixture", "source text", 1, DateTimeOffset.UtcNow, null, null, null, SourceMaterialAccessStatus.OpenAccess, false, []);
        var extraction = new EvidenceExtraction(Guid.NewGuid(), run.Id, study.Id, material.Id, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "fake", "fake", "fixture-v1", DateTimeOffset.UtcNow, 1, true);
        var evidence = new Evidence(Guid.NewGuid(), run.Id, study.Id, extraction.Id, "outcome", "result", "source text", EvidenceDirection.Positive, EvidenceSourceScope.Abstract, DateTimeOffset.UtcNow, true, "population", "intervention", "control", "trial", 100, "odds ratio", 1.25m, 1.0m, 1.5m, 0.05m, 0.95m, 0.1m);
        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        context.Studies.Add(study);
        context.SourceMaterials.Add(material);
        context.EvidenceExtractions.Add(extraction);
        context.Evidence.Add(evidence);
        await context.SaveChangesAsync();
        return new Seed(run.Id, study.Id, material.Id, extraction.Id, evidence.Id);
    }

    private static QuantitativeSynthesisResult CreateResult(Guid runId, QuantitativeSynthesisContribution contribution)
    {
        return new QuantitativeSynthesisResult(runId, "outcome|population|control|trial|OddsRatio", "outcome", "population", "control", "trial", EffectMeasureType.OddsRatio, QuantitativeSynthesisStatus.Synthesized, QuantitativeSynthesisMethod.FixedEffectInverseVariance, "fixed-effect-inverse-variance-v1", 0.95m, 1, 1, 0.25d, 0.5d, Math.Sqrt(0.5d), -1d, 1.5d, Math.Exp(0.25d), Math.Exp(-1d), Math.Exp(1.5d), null, null, null, [contribution], []);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
    }

    private sealed record Seed(Guid RunId, Guid StudyId, Guid SourceMaterialId, Guid ExtractionId, Guid EvidenceId);
}

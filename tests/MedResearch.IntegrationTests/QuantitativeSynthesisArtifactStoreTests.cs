using MedResearch.Application.Research.Quantitative;
using MedResearch.Domain;
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
            context.ResearchRuns.Add(new ResearchRun(otherRunId, question.CreatedAt));
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

    private async Task<Seed> SeedAsync()
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion("Does the intervention change the outcome?", DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);
        var study = new Study(Guid.NewGuid(), "Study title", "Abstract", "10.1000/example", "12345", "Journal", new DateOnly(2025, 1, 2), "PubMed");
        var material = SourceMaterial.Create(study.Id, SourceMaterialType.Abstract, "PubMed", "12345", "fixture", "source text", 1, DateTimeOffset.UtcNow, null, null, null, SourceMaterialAccessStatus.OpenAccess, false, []);
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

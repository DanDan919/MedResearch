using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using Xunit;

namespace MedResearch.Application.Tests;

public sealed class EvidenceCorpusBuilderTests
{
    [Fact]
    public async Task BuildAsync_RejectsEvidenceFromAnotherResearchRun()
    {
        var runId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId) with
        {
            Evidence = [CreateEvidence(Guid.NewGuid(), CreateSnapshot(runId).Studies.Single().StudyId, Guid.NewGuid(), EvidenceDirection.Positive, Guid.NewGuid())]
        };

        var builder = new EvidenceCorpusBuilder(new StaticStore(snapshot));

        var exception = await Assert.ThrowsAsync<ResearchSynthesisValidationException>(
            () => builder.BuildAsync(runId, CancellationToken.None));

        Assert.Contains("outside the current ResearchRun", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_RejectsSourceMaterialFromAnotherStudy()
    {
        var runId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId);
        var source = snapshot.SourceMaterials.Single() with { StudyId = Guid.NewGuid() };
        var invalid = snapshot with { SourceMaterials = [source] };

        var builder = new EvidenceCorpusBuilder(new StaticStore(invalid));

        var exception = await Assert.ThrowsAsync<ResearchSynthesisValidationException>(
            () => builder.BuildAsync(runId, CancellationToken.None));

        Assert.Contains("SourceMaterial", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_RejectsEvidenceWithoutGroundedExtractionLineage()
    {
        var runId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId);
        var evidence = snapshot.Evidence.Single() with { EvidenceExtractionId = Guid.NewGuid() };
        var invalid = snapshot with { Evidence = [evidence] };

        var builder = new EvidenceCorpusBuilder(new StaticStore(invalid));

        var exception = await Assert.ThrowsAsync<ResearchSynthesisValidationException>(
            () => builder.BuildAsync(runId, CancellationToken.None));

        Assert.Contains("grounded completed extraction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_RejectsSearchProvenanceFromAnotherResearchRun()
    {
        var runId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId);
        var search = snapshot.Searches.Single() with { ResearchRunId = Guid.NewGuid() };
        var invalid = snapshot with { Searches = [search] };

        var builder = new EvidenceCorpusBuilder(new StaticStore(invalid));

        var exception = await Assert.ThrowsAsync<ResearchSynthesisValidationException>(
            () => builder.BuildAsync(runId, CancellationToken.None));

        Assert.Contains("Search provenance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesStudyAndGroupsNormalizedConflictingOutcomes()
    {
        var runId = Guid.NewGuid();
        var snapshot = CreateSnapshot(runId, includeNegativeEvidence: true);

        var corpus = await new EvidenceCorpusBuilder(new StaticStore(snapshot))
            .BuildAsync(runId, CancellationToken.None);

        Assert.Single(corpus.Studies);
        Assert.Equal(2, corpus.Evidence.Count);
        var group = Assert.Single(corpus.OutcomeGroups);
        Assert.Equal("recall", group.Outcome);
        Assert.True(group.HasConflict);
        Assert.Equal(1, corpus.Coverage.StudiesWithEvidence);
        Assert.Equal(1, corpus.Coverage.ConflictOutcomeCount);
    }

    private static SynthesisCorpusSnapshot CreateSnapshot(Guid runId, bool includeNegativeEvidence = false)
    {
        var studyId = Guid.NewGuid();
        var extractionId = Guid.NewGuid();
        var sourceMaterialId = Guid.NewGuid();
        var firstEvidenceId = Guid.NewGuid();
        var evidence = new List<SynthesisEvidenceContext>
        {
            CreateEvidence(runId, studyId, firstEvidenceId, EvidenceDirection.Positive, extractionId)
        };

        if (includeNegativeEvidence)
        {
            evidence.Add(CreateEvidence(runId, studyId, Guid.NewGuid(), EvidenceDirection.Negative, extractionId, " recall "));
        }

        return new SynthesisCorpusSnapshot(
            runId,
            Guid.NewGuid(),
            "Does sleep improve recall?",
            null,
            [new SynthesisStudySnapshot(
                studyId,
                "Study title",
                "12345678",
                "PMC123456",
                "10.1000/example",
                "Journal",
                new DateOnly(2026, 1, 1),
                ["Journal Article"],
                ["Ada Lovelace"],
                "PubMed",
                DateTimeOffset.UtcNow)],
            evidence,
            [],
            [new SynthesisSearchSnapshot(Guid.NewGuid(), runId, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0)],
            [new SynthesisExtractionSnapshot(
                extractionId,
                runId,
                studyId,
                EvidenceExtractionStatus.Completed,
                null,
                EvidenceSourceScope.Abstract,
                sourceMaterialId,
                evidence.Count,
                true)],
            [new SynthesisSourceMaterialSnapshot(
                sourceMaterialId,
                studyId,
                SourceMaterialType.Abstract,
                "PubMed",
                "12345678",
                SourceMaterial.ComputeContentHash("Recall improved."),
                1,
                false,
                true)]);
    }

    private static SynthesisEvidenceContext CreateEvidence(
        Guid runId,
        Guid studyId,
        Guid evidenceId,
        EvidenceDirection direction,
        Guid extractionId,
        string outcome = "Recall")
    {
        return new SynthesisEvidenceContext(
            evidenceId,
            runId,
            studyId,
            extractionId,
            outcome,
            "Recall improved.",
            "Recall improved.",
            direction,
            EvidenceSourceScope.Abstract,
            DateTimeOffset.UtcNow,
            "adults",
            "sleep",
            "wakefulness",
            "controlled trial",
            120,
            null,
            null,
            null,
            null,
            null);
    }

    private sealed class StaticStore : ISynthesisCorpusStore
    {
        private readonly SynthesisCorpusSnapshot _snapshot;

        public StaticStore(SynthesisCorpusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }
    }
}

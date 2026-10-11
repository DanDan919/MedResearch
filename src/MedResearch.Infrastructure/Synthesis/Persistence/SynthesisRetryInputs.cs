using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Infrastructure.Synthesis.Persistence;

public sealed class SynthesisRetryInputs(
    MedResearchDbContext db,
    SynthesisOptions options,
    IQuantitativeStatisticalSynthesizer statistics)
{
    public async Task<string> ValidateAndFingerprintAsync(Guid runId, string owner, CancellationToken token)
    {
        var store = new EfResearchSynthesisStore(db, options: options);
        var snapshot = await store.LoadCorpusAsync(runId, token);
        var plan = await db.ResearchPlans.AsNoTracking().SingleOrDefaultAsync(x => x.ResearchRunId == runId, token);
        if (plan is null || plan.ResearchQuestionId != snapshot.ResearchQuestionId || plan.OriginalQuestion != snapshot.ResearchQuestion
            || snapshot.Searches.Count == 0 || snapshot.Evidence.Count == 0
            || await db.Evidence.CountAsync(x => x.ResearchRunId == runId, token) != snapshot.Evidence.Count
            || await db.EvidenceExtractions.CountAsync(x => x.ResearchRunId == runId, token) != snapshot.Extractions.Count
            || await db.EvidenceEvaluations.CountAsync(x => x.ResearchRunId == runId, token) != snapshot.Evaluations.Count)
            throw new SynthesisRetryConflictException();

        var searches = await db.LiteratureSearches.AsNoTracking().Where(x => x.ResearchRunId == runId).ToArrayAsync(token);
        var discoveries = await db.ResearchStudyDiscoveries.AsNoTracking().Where(x => x.ResearchRunId == runId).OrderBy(x => x.Id).ToArrayAsync(token);
        if (searches.Any(x => x.ResearchPlanId != plan.Id)
            || discoveries.Any(x => !searches.Any(s => s.Id == x.LiteratureSearchId && s.Source == x.Source)))
            throw new SynthesisRetryConflictException();

        var corpus = await new EvidenceCorpusBuilder(store).BuildAsync(runId, token);
        if (snapshot.Extractions.Any(x => x.Status == EvidenceExtractionStatus.Completed &&
            x.EvidenceCount != snapshot.Evidence.Count(evidence => evidence.EvidenceExtractionId == x.ExtractionId)))
            throw new SynthesisRetryConflictException();
        var grounding = new EvidenceGroundingValidator();
        foreach (var evidence in corpus.Evidence)
        {
            var extraction = snapshot.Extractions.Single(x => x.ExtractionId == evidence.EvidenceExtractionId);
            var source = snapshot.SourceMaterials.Single(x => x.SourceMaterialId == extraction.SourceMaterialId);
            if (source.Content is null || SourceMaterial.ComputeContentHash(SourceMaterial.NormalizeContent(source.Content)) != source.ContentHash
                || !grounding.IsGrounded(source.Content, evidence.SupportingText)
                || !snapshot.Evaluations.Any(x => x.StudyId == evidence.StudyId && x.Status == EvidenceEvaluationStatus.Completed && x.EvidenceIds.Contains(evidence.EvidenceId)))
                throw new SynthesisRetryConflictException();
        }

        // Recompute only to compare immutable artifacts; this adapter never inserts a snapshot.
        var artifacts = new ReadOnlyArtifacts(new EfQuantitativeSynthesisArtifactStore(db), owner);
        var context = await new SynthesisContextBuilder(store, options, NullLogger<SynthesisContextBuilder>.Instance,
            quantitativeStatisticalSynthesizer: statistics, quantitativeSynthesisArtifactStore: artifacts).BuildAsync(runId, token);
        new ResearchReportDraftValidator(options).ValidateContext(context);
        return Hash(JsonSerializer.Serialize(new { snapshot, discoveries, artifacts = context.QuantitativeArtifacts }));
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ReadOnlyArtifacts(IQuantitativeSynthesisArtifactStore store, string owner) : IQuantitativeSynthesisArtifactStore
    {
        public async Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> PersistAsync(QuantitativeSynthesisReadiness readiness, CancellationToken token)
        {
            var existing = (await store.FindByResearchRunIdAsync(readiness.ResearchRunId, owner, token)).OrderBy(x => x.Result.GroupKey, StringComparer.Ordinal).ToArray();
            if (existing.Length != readiness.Results.Count || readiness.Results.Any(result => !existing.Any(x =>
                x.Result.GroupKey == result.GroupKey && x.SnapshotFingerprint == QuantitativeSynthesisArtifactSnapshot.ComputeFingerprint(result))))
                throw new SynthesisRetryConflictException();
            return existing;
        }

        public Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> FindByResearchRunIdAsync(Guid runId, string actor, CancellationToken token) => store.FindByResearchRunIdAsync(runId, actor, token);
    }
}

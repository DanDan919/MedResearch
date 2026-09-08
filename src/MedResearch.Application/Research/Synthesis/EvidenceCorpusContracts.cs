using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public sealed record SynthesisSourceMaterialSnapshot(
    Guid SourceMaterialId,
    Guid StudyId,
    SourceMaterialType Type,
    string Provider,
    string? ProviderSourceId,
    string ContentHash,
    int ContentVersion,
    bool WasTruncated,
    bool IsCurrent);

public sealed record EvidenceCorpusCoverage(
    int StudiesDiscovered,
    int StudiesWithSourceMaterial,
    int StudiesWithStructuredFullText,
    int StudiesAbstractOnly,
    int StudiesWithoutSource,
    int StudiesWithEvidence,
    int StudiesSkipped,
    int EvidenceFindingCount,
    int EvaluatedStudyCount,
    int ConflictOutcomeCount);

public sealed record EvidenceCorpusOutcomeGroup(
    string Outcome,
    IReadOnlyCollection<Guid> EvidenceIds,
    IReadOnlyCollection<EvidenceDirection> Directions,
    bool HasConflict);

public sealed record EvidenceCorpus(
    Guid ResearchRunId,
    SynthesisCorpusSnapshot Snapshot,
    IReadOnlyCollection<SynthesisStudySnapshot> Studies,
    IReadOnlyCollection<SynthesisEvidenceContext> Evidence,
    IReadOnlyCollection<SynthesisEvaluationContext> Evaluations,
    IReadOnlyCollection<SynthesisExtractionSnapshot> Extractions,
    IReadOnlyCollection<SynthesisSourceMaterialSnapshot> SourceMaterials,
    IReadOnlyCollection<SynthesisSearchSnapshot> Searches,
    IReadOnlyCollection<EvidenceCorpusOutcomeGroup> OutcomeGroups,
    EvidenceCorpusCoverage Coverage);

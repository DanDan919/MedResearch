using MedResearch.Application.Research.Quantitative;

namespace MedResearch.Infrastructure.Persistence;

public sealed class QuantitativeSynthesisArtifactEntity
{
    public Guid Id { get; set; }

    public Guid ResearchRunId { get; set; }

    public string GroupKey { get; set; } = null!;

    public QuantitativeSynthesisStatus Status { get; set; }

    public string AlgorithmVersion { get; set; } = null!;

    public decimal OutputConfidenceLevel { get; set; }

    public int EvidenceCount { get; set; }

    public int UniqueStudyCount { get; set; }

    public string SnapshotFingerprint { get; set; } = null!;

    public string SnapshotJson { get; set; } = null!;

    public DateTimeOffset PersistedAt { get; set; }
}

public sealed class QuantitativeSynthesisContributionSnapshotEntity
{
    public Guid ArtifactId { get; set; }

    public string AnalysisMethod { get; set; } = null!;

    public int Ordinal { get; set; }

    public Guid EvidenceId { get; set; }

    public Guid StudyId { get; set; }

    public Guid EvidenceExtractionId { get; set; }

    public Guid SourceMaterialId { get; set; }

    public double AnalysisScaleEffect { get; set; }

    public double AnalysisScaleVariance { get; set; }

    public double AnalysisScaleStandardError { get; set; }

    public double Weight { get; set; }

    public double NormalizedWeight { get; set; }
}

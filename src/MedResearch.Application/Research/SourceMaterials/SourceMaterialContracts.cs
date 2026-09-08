using MedResearch.Domain;

namespace MedResearch.Application.Research.SourceMaterials;

public sealed record SourceMaterialStudyContext(
    Guid ResearchRunId,
    Guid StudyId,
    string Title,
    string? Pmid,
    string? Pmcid,
    string? Doi,
    string? Abstract,
    string Source);

public sealed record SourceMaterialCandidate(
    SourceMaterialType Type,
    string Provider,
    string? ProviderSourceId,
    string RetrievalMethod,
    string Content,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? SourceUpdatedAt,
    string? License,
    string? LicenseUrl,
    SourceMaterialAccessStatus AccessStatus,
    bool WasTruncated,
    IReadOnlyCollection<string> SectionNames);

public sealed record SourceMaterialAcquisitionStudySet(
    int TotalDiscoveredStudyCount,
    IReadOnlyCollection<SourceMaterialStudyContext> Studies);

public sealed record SourceMaterialPersistenceResult(
    Guid SourceMaterialId,
    bool Created,
    bool Reused,
    bool NewVersionCreated,
    string ContentHash,
    int ContentVersion);

public sealed record SourceMaterialAcquisitionResult(
    int SelectedStudyCount,
    int AbstractMaterialCount,
    int StructuredFullTextCount,
    int ReusedMaterialCount,
    int UnavailableFullTextCount,
    int ProviderFailureCount);

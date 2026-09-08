using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.SourceMaterials;

public sealed class SourceMaterialAcquirer : ISourceMaterialAcquirer
{
    private readonly ISourceMaterialStore _store;
    private readonly IReadOnlyCollection<ISourceMaterialProvider> _providers;
    private readonly SourceAcquisitionOptions _options;
    private readonly ILogger<SourceMaterialAcquirer> _logger;

    public SourceMaterialAcquirer(
        ISourceMaterialStore store,
        IEnumerable<ISourceMaterialProvider> providers,
        SourceAcquisitionOptions options,
        ILogger<SourceMaterialAcquirer> logger)
    {
        _store = store;
        _providers = providers.ToArray();
        _options = options;
        _logger = logger;
    }

    public async Task<SourceMaterialAcquisitionResult> AcquireForResearchRunAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            _logger.LogInformation("SourceAcquisitionDisabled. ResearchRunId: {ResearchRunId}", researchRunId);
            return new SourceMaterialAcquisitionResult(0, 0, 0, 0, 0, 0);
        }

        var studies = await _store.FindStudiesForSourceAcquisitionAsync(
            researchRunId,
            _options.BoundedMaxStudiesPerRun,
            cancellationToken);

        var abstractCount = 0;
        var structuredFullTextCount = 0;
        var reusedCount = 0;
        var unavailableCount = 0;
        var failureCount = 0;

        foreach (var study in studies.Studies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "SourceAcquisitionStarted. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; ProviderCount: {ProviderCount}",
                researchRunId,
                study.StudyId,
                _providers.Count);

            if (!string.IsNullOrWhiteSpace(study.Abstract))
            {
                var abstractResult = await _store.PersistSourceMaterialAsync(
                    study.StudyId,
                    new SourceMaterialCandidate(
                        SourceMaterialType.Abstract,
                        study.Source,
                        study.Pmid ?? study.Pmcid ?? study.Doi,
                        "SearchMetadataAbstract",
                        study.Abstract,
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        null,
                        SourceMaterialAccessStatus.Unknown,
                        false,
                        ["Abstract"]),
                    cancellationToken);

                if (abstractResult.Created || abstractResult.NewVersionCreated)
                {
                    abstractCount++;
                }
                else if (abstractResult.Reused)
                {
                    reusedCount++;
                }
            }

            foreach (var provider in _providers)
            {
                try
                {
                    var candidate = await provider.TryAcquireAsync(study, _options.BoundedMaxContentCharacters, cancellationToken);
                    if (candidate is null)
                    {
                        unavailableCount++;
                        _logger.LogInformation(
                            "SourceMaterialUnavailable. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; Provider: {Provider}",
                            researchRunId,
                            study.StudyId,
                            provider.ProviderName);
                        continue;
                    }

                    var result = await _store.PersistSourceMaterialAsync(study.StudyId, candidate, cancellationToken);
                    if (result.Created || result.NewVersionCreated)
                    {
                        if (candidate.Type == SourceMaterialType.StructuredFullText)
                        {
                            structuredFullTextCount++;
                        }
                        else
                        {
                            abstractCount++;
                        }

                        _logger.LogInformation(
                            "SourceMaterialVersionCreated. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; SourceType: {SourceType}; Provider: {Provider}; ContentHash: {ContentHash}; ContentVersion: {ContentVersion}; WasTruncated: {WasTruncated}",
                            researchRunId,
                            study.StudyId,
                            result.SourceMaterialId,
                            candidate.Type,
                            candidate.Provider,
                            result.ContentHash,
                            result.ContentVersion,
                            candidate.WasTruncated);
                    }
                    else if (result.Reused)
                    {
                        reusedCount++;
                        _logger.LogInformation(
                            "SourceMaterialReused. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; SourceMaterialId: {SourceMaterialId}; SourceType: {SourceType}; Provider: {Provider}; ContentHash: {ContentHash}",
                            researchRunId,
                            study.StudyId,
                            result.SourceMaterialId,
                            candidate.Type,
                            candidate.Provider,
                            result.ContentHash);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failureCount++;
                    _logger.LogWarning(
                        exception,
                        "SourceAcquisitionFailed. ResearchRunId: {ResearchRunId}; StudyId: {StudyId}; Provider: {Provider}",
                        researchRunId,
                        study.StudyId,
                        provider.ProviderName);
                }
            }
        }

        return new SourceMaterialAcquisitionResult(
            studies.Studies.Count,
            abstractCount,
            structuredFullTextCount,
            reusedCount,
            unavailableCount,
            failureCount);
    }
}

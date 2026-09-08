using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.SourceMaterials;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Extraction.Persistence;

public sealed class EfEvidenceExtractionStore : IEvidenceExtractionStore
{
    private readonly MedResearchDbContext _dbContext;
    private readonly SourceAcquisitionOptions _sourceOptions;

    public EfEvidenceExtractionStore(MedResearchDbContext dbContext)
        : this(dbContext, new SourceAcquisitionOptions())
    {
    }

    public EfEvidenceExtractionStore(MedResearchDbContext dbContext, SourceAcquisitionOptions sourceOptions)
    {
        _dbContext = dbContext;
        _sourceOptions = sourceOptions;
    }

    public async Task<EvidenceExtractionWorkItemSet> FindStudiesForExtractionAsync(
        Guid researchRunId,
        string promptVersion,
        int maxStudies,
        CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        if (maxStudies <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStudies), "Max studies must be positive.");
        }

        var plan = await _dbContext.ResearchPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(plan => plan.ResearchRunId == researchRunId, cancellationToken);
        var runAndQuestion = await _dbContext.ResearchRuns
            .AsNoTracking()
            .Join(
                _dbContext.ResearchQuestions.AsNoTracking(),
                run => run.ResearchQuestionId,
                question => question.Id,
                (run, question) => new
                {
                    ResearchRunId = run.Id,
                    ResearchQuestionId = question.Id,
                    ResearchQuestion = question.Text
                })
            .SingleAsync(item => item.ResearchRunId == researchRunId, cancellationToken);

        var discovered = await _dbContext.ResearchStudyDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ResearchRunId == researchRunId)
            .GroupBy(discovery => discovery.StudyId)
            .Select(group => new
            {
                StudyId = group.Key,
                DiscoveredAt = group.Min(discovery => discovery.DiscoveredAt)
            })
            .Join(
                _dbContext.Studies.AsNoTracking(),
                discovery => discovery.StudyId,
                study => study.Id,
                (discovery, study) => new { discovery, study })
            .OrderBy(item => item.discovery.DiscoveredAt)
            .ThenBy(item => item.study.Pmid)
            .ThenBy(item => item.study.Pmcid)
            .ThenBy(item => item.study.Doi)
            .ThenBy(item => item.study.Id)
            .ToArrayAsync(cancellationToken);

        var totalCount = discovered.Length;
        var studyIds = discovered.Select(item => item.study.Id).ToArray();
        var sourceMaterials = await _dbContext.SourceMaterials
            .AsNoTracking()
            .Where(material => studyIds.Contains(material.StudyId) && material.IsCurrent)
            .ToArrayAsync(cancellationToken);
        var sourceMaterialsByStudy = sourceMaterials
            .GroupBy(material => material.StudyId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var existingExtractions = await _dbContext.EvidenceExtractions
            .AsNoTracking()
            .Where(extraction => extraction.ResearchRunId == researchRunId && extraction.PromptVersion == promptVersion)
            .ToArrayAsync(cancellationToken);

        var planContext = plan == null
            ? null
            : new EvidenceExtractionPlanContext(
                plan.Population,
                plan.ExposureOrIntervention,
                plan.Comparator,
                plan.Outcomes,
                plan.PreferredStudyTypes,
                plan.ExclusionHints);

        var studies = new List<EvidenceExtractionStudyContext>();
        foreach (var item in discovered)
        {
            var selectedSource = sourceMaterialsByStudy.TryGetValue(item.study.Id, out var materials)
                ? SelectBestSourceMaterial(materials)
                : null;

            var alreadyExtracted = existingExtractions.Any(extraction =>
                extraction.StudyId == item.study.Id
                && extraction.SourceMaterialId == selectedSource?.Id);
            if (alreadyExtracted)
            {
                continue;
            }

            studies.Add(new EvidenceExtractionStudyContext(
                researchRunId,
                runAndQuestion.ResearchQuestionId,
                runAndQuestion.ResearchQuestion,
                planContext,
                item.study.Id,
                selectedSource?.Id,
                selectedSource is null ? EvidenceSourceScope.Abstract : ToEvidenceSourceScope(selectedSource.Type),
                selectedSource?.Provider,
                selectedSource?.Content,
                selectedSource?.ContentHash,
                selectedSource?.WasTruncated ?? false,
                selectedSource?.SectionNames ?? [],
                item.study.Title,
                item.study.Abstract,
                item.study.Pmid,
                item.study.Pmcid,
                item.study.Doi,
                item.study.Journal,
                item.study.PublicationDate,
                item.study.PublicationTypes,
                item.study.Authors,
                item.study.Source));

            if (studies.Count >= maxStudies)
            {
                break;
            }
        }

        return new EvidenceExtractionWorkItemSet(totalCount, studies);
    }

    public async Task PersistExtractionResultAsync(
        EvidenceExtractionResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (result.SourceMaterialId.HasValue)
        {
            var sourceMaterialStudyId = await _dbContext.SourceMaterials
                .AsNoTracking()
                .Where(material => material.Id == result.SourceMaterialId.Value)
                .Select(material => material.StudyId)
                .SingleAsync(cancellationToken);
            if (sourceMaterialStudyId != result.StudyId)
            {
                throw new InvalidOperationException("Evidence extraction source material must belong to the extraction study.");
            }
        }

        var existingExtraction = await _dbContext.EvidenceExtractions
            .SingleOrDefaultAsync(extraction =>
                extraction.ResearchRunId == result.ResearchRunId
                && extraction.StudyId == result.StudyId
                && extraction.SourceMaterialId == result.SourceMaterialId
                && extraction.PromptVersion == result.PromptVersion,
                cancellationToken);

        if (existingExtraction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var extraction = new EvidenceExtraction(
            Guid.NewGuid(),
            result.ResearchRunId,
            result.StudyId,
            result.SourceMaterialId,
            result.Status,
            result.SkipReason,
            result.SourceScope,
            result.Provider,
            result.Model,
            result.PromptVersion,
            result.ExtractedAt,
            result.Findings.Count,
            result.GroundingValidated);

        _dbContext.EvidenceExtractions.Add(extraction);

        foreach (var finding in result.Findings)
        {
            _dbContext.Evidence.Add(new Evidence(
                Guid.NewGuid(),
                result.ResearchRunId,
                result.StudyId,
                extraction.Id,
                finding.Outcome,
                finding.ResultSummary,
                finding.SupportingText,
                finding.Direction,
                result.SourceScope,
                result.ExtractedAt,
                result.GroundingValidated,
                finding.Population,
                finding.ExposureOrIntervention,
                finding.Comparator,
                finding.StudyDesign,
                finding.SampleSize,
                finding.EffectMeasure,
                finding.EffectValue,
                finding.ConfidenceIntervalLower,
                finding.ConfidenceIntervalUpper,
                finding.PValue));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private SourceMaterial? SelectBestSourceMaterial(IReadOnlyCollection<SourceMaterial> sourceMaterials)
    {
        return sourceMaterials
            .Where(material => !string.IsNullOrWhiteSpace(material.Content))
            .OrderBy(material => _sourceOptions.PreferStructuredFullText && material.Type == SourceMaterialType.StructuredFullText ? 0 : 1)
            .ThenBy(material => material.WasTruncated)
            .ThenByDescending(material => material.ContentVersion)
            .ThenBy(material => material.Provider)
            .ThenBy(material => material.Id)
            .FirstOrDefault();
    }

    private static EvidenceSourceScope ToEvidenceSourceScope(SourceMaterialType type)
    {
        return type == SourceMaterialType.StructuredFullText
            ? EvidenceSourceScope.StructuredFullText
            : EvidenceSourceScope.Abstract;
    }
}

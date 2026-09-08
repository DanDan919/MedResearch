using MedResearch.Domain;
using Microsoft.Extensions.Logging;

namespace MedResearch.Application.Research.Synthesis;

public sealed class SynthesisContextBuilder : ISynthesisContextBuilder
{
    private readonly IEvidenceCorpusBuilder _evidenceCorpusBuilder;
    private readonly SynthesisOptions _options;
    private readonly ILogger<SynthesisContextBuilder> _logger;

    public SynthesisContextBuilder(
        ISynthesisCorpusStore corpusStore,
        SynthesisOptions options,
        ILogger<SynthesisContextBuilder> logger,
        IEvidenceCorpusBuilder? evidenceCorpusBuilder = null)
    {
        _evidenceCorpusBuilder = evidenceCorpusBuilder ?? new EvidenceCorpusBuilder(corpusStore);
        _options = options;
        _logger = logger;
    }

    public async Task<SynthesisContext> BuildAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var corpus = await _evidenceCorpusBuilder.BuildAsync(researchRunId, cancellationToken);
        var snapshot = corpus.Snapshot;

        var validatedEvidence = snapshot.Evidence
            .Where(evidence => evidence.EvidenceId != Guid.Empty)
            .OrderBy(evidence => evidence.ExtractedAt)
            .ThenBy(evidence => evidence.StudyId)
            .ThenBy(evidence => evidence.EvidenceId)
            .ToArray();
        var studiesWithValidatedEvidence = validatedEvidence
            .Select(evidence => evidence.StudyId)
            .Distinct()
            .ToHashSet();
        var studyOrder = snapshot.Studies
            .OrderBy(study => study.DiscoveredAt)
            .ThenBy(study => study.Pmid)
            .ThenBy(study => study.Pmcid)
            .ThenBy(study => study.Doi)
            .ThenBy(study => study.StudyId)
            .Where(study => studiesWithValidatedEvidence.Contains(study.StudyId))
            .Take(_options.BoundedMaxStudies)
            .Select(study => study.StudyId)
            .ToArray();
        var selectedStudyIds = studyOrder.ToHashSet();
        var selectedEvidence = validatedEvidence
            .Where(evidence => selectedStudyIds.Contains(evidence.StudyId))
            .Take(_options.BoundedMaxEvidenceFindings)
            .ToArray();
        selectedStudyIds = selectedEvidence.Select(evidence => evidence.StudyId).ToHashSet();

        var evidenceTruncated = validatedEvidence.Length > selectedEvidence.Length
            || studiesWithValidatedEvidence.Count > selectedStudyIds.Count;
        var selectedEvaluations = snapshot.Evaluations
            .Where(evaluation => selectedStudyIds.Contains(evaluation.StudyId))
            .OrderBy(evaluation => evaluation.StudyId)
            .ToDictionary(evaluation => evaluation.StudyId, evaluation => evaluation);
        var evidenceByStudyId = selectedEvidence
            .GroupBy(evidence => evidence.StudyId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<SynthesisEvidenceContext>)group.ToArray());

        var studies = snapshot.Studies
            .Where(study => selectedStudyIds.Contains(study.StudyId))
            .OrderBy(study => Array.IndexOf(studyOrder, study.StudyId))
            .ThenBy(study => study.Pmid)
            .ThenBy(study => study.Pmcid)
            .ThenBy(study => study.Doi)
            .ThenBy(study => study.StudyId)
            .Select(study =>
            {
                selectedEvaluations.TryGetValue(study.StudyId, out var evaluation);
                evidenceByStudyId.TryGetValue(study.StudyId, out var evidence);
                evidence ??= [];

                return new SynthesisStudyContext(
                    study.StudyId,
                    study.Title,
                    study.Pmid,
                    study.Pmcid,
                    study.Doi,
                    study.Journal,
                    study.PublicationDate,
                    study.PublicationTypes,
                    study.Authors,
                    study.Source,
                    evaluation,
                    evidence);
            })
            .ToArray();

        var outcomeSummaries = BuildOutcomeSummaries(selectedEvidence);
        var searchedSources = snapshot.Searches
            .Select(search => NormalizeOptional(search.Source))
            .Where(source => source is not null)
            .Select(source => source!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceScopes = selectedEvidence.Select(evidence => evidence.SourceScope).Distinct().ToArray();
        var usesAbstractOnly = selectedEvidence.Length == 0
            || sourceScopes.All(scope => scope == EvidenceSourceScope.Abstract);

        var completedExtractions = snapshot.Extractions
            .Where(extraction => extraction.Status == EvidenceExtractionStatus.Completed)
            .ToArray();
        var noSourceMaterialStudyCount = snapshot.Extractions
            .Where(extraction => extraction.Status == EvidenceExtractionStatus.Skipped && extraction.SkipReason == EvidenceExtractionSkipReason.NoExtractableText)
            .Select(extraction => extraction.StudyId)
            .Distinct()
            .Count();
        var statistics = new SynthesisCorpusStatistics(
            snapshot.Studies.Count,
            completedExtractions.Length,
            snapshot.Evaluations.Count(evaluation => evaluation.Status == EvidenceEvaluationStatus.Completed),
            validatedEvidence.Length,
            studies.Length,
            selectedEvidence.Length,
            snapshot.Searches.Count,
            noSourceMaterialStudyCount,
            snapshot.Evaluations.Count(evaluation => evaluation.InsufficientSourceDomainCount > 0 || evaluation.Status == EvidenceEvaluationStatus.Skipped),
            completedExtractions
                .Where(extraction => extraction.SourceScope is EvidenceSourceScope.FullText or EvidenceSourceScope.StructuredFullText)
                .Select(extraction => extraction.StudyId)
                .Distinct()
                .Count(),
            completedExtractions
                .Where(extraction => extraction.SourceScope == EvidenceSourceScope.Abstract)
                .Select(extraction => extraction.StudyId)
                .Distinct()
                .Count(),
            noSourceMaterialStudyCount);
        var potentialConflictDetected = outcomeSummaries.Any(summary => summary.ConflictStatus == SynthesisConflictStatus.Present);
        var coverage = new SynthesisSourceCoverage(
            searchedSources,
            usesAbstractOnly,
            selectedEvidence.Any(evidence => evidence.SourceScope is EvidenceSourceScope.FullText or EvidenceSourceScope.StructuredFullText),
            evidenceTruncated,
            potentialConflictDetected,
            snapshot.Searches.Count);
        var limitations = BuildLimitations(coverage, statistics, outcomeSummaries, evidenceTruncated);

        var context = new SynthesisContext(
            snapshot.ResearchRunId,
            snapshot.ResearchQuestionId,
            snapshot.ResearchQuestion,
            snapshot.Plan,
            statistics,
            coverage,
            studies,
            outcomeSummaries,
            limitations);

        _logger.LogInformation(
            "SynthesisContextBuilt. ResearchRunId: {ResearchRunId}; StudyCount: {StudyCount}; EvidenceCount: {EvidenceCount}; EvaluationCount: {EvaluationCount}; ConflictCount: {ConflictCount}; EvidenceTruncated: {EvidenceTruncated}",
            context.ResearchRunId,
            context.Statistics.IncludedStudyCount,
            context.Statistics.IncludedEvidenceFindingCount,
            context.Statistics.EvaluatedStudyCount,
            context.OutcomeDirectionSummaries.Count(summary => summary.ConflictStatus == SynthesisConflictStatus.Present),
            context.SourceCoverage.EvidenceTruncated);

        return context;
    }

    private static IReadOnlyCollection<SynthesisOutcomeDirectionSummary> BuildOutcomeSummaries(IReadOnlyCollection<SynthesisEvidenceContext> evidence)
    {
        return evidence
            .GroupBy(evidence => NormalizeOutcome(evidence.Outcome), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var positive = items.Count(item => item.Direction == EvidenceDirection.Positive);
                var negative = items.Count(item => item.Direction == EvidenceDirection.Negative);
                var noClear = items.Count(item => item.Direction == EvidenceDirection.NoClearEffect);
                var mixed = items.Count(item => item.Direction == EvidenceDirection.Mixed);
                var notReported = items.Count(item => item.Direction == EvidenceDirection.NotReported);
                var conflict = positive > 0 && negative > 0
                    ? SynthesisConflictStatus.Present
                    : items.Length > 1 ? SynthesisConflictStatus.NotDetected : SynthesisConflictStatus.Unknown;

                return new SynthesisOutcomeDirectionSummary(group.Key, positive, negative, noClear, mixed, notReported, conflict);
            })
            .ToArray();
    }

    private static IReadOnlyCollection<string> BuildLimitations(
        SynthesisSourceCoverage coverage,
        SynthesisCorpusStatistics statistics,
        IReadOnlyCollection<SynthesisOutcomeDirectionSummary> outcomeSummaries,
        bool evidenceTruncated)
    {
        var limitations = new List<string>();

        if (coverage.SearchedSources.Count == 0)
        {
            limitations.Add("No persisted literature search provenance is available for this research run.");
        }
        else if (coverage.SearchedSources.Count == 1 && coverage.SearchedSources.Contains("PubMed", StringComparer.OrdinalIgnoreCase))
        {
            limitations.Add("Search coverage is limited to PubMed provenance currently persisted by MedResearch.");
        }
        else
        {
            limitations.Add("Search coverage reflects only persisted MedResearch literature-search provenance.");
        }

        if (coverage.UsesAbstractLevelEvidenceOnly)
        {
            limitations.Add("Current evidence is abstract-level; full-text review and formal risk-of-bias assessment were not performed.");
        }

        if (statistics.StructuredFullTextStudyCount > 0 && statistics.AbstractOnlyStudyCount > 0)
        {
            limitations.Add("Some included publications were processed from abstract-only source material.");
        }

        if (evidenceTruncated)
        {
            limitations.Add("The synthesis context was bounded by configured study/evidence limits using deterministic ordering.");
        }

        if (statistics.StudiesWithInsufficientEvaluationSource > 0)
        {
            limitations.Add("Some methodological evaluation domains are limited by insufficient source detail, not necessarily by source-supported study flaws.");
        }

        limitations.Add("Different Study records may describe overlapping participant populations; MedResearch does not yet detect cohort overlap.");
        limitations.Add("Systematic reviews, meta-analyses, and primary studies are preserved by study design but citation-network overlap is not resolved.");
        limitations.Add("Direction counts are descriptive corpus context only and are not statistical weights or certainty estimates.");

        if (outcomeSummaries.Any(summary => summary.ConflictStatus == SynthesisConflictStatus.Present))
        {
            limitations.Add("Potentially conflicting findings are represented without forcing a single winning direction.");
        }

        return limitations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeOutcome(string value)
    {
        return string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }
}

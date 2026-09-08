using MedResearch.Domain;

namespace MedResearch.Application.Research.Synthesis;

public sealed class EvidenceCorpusBuilder : IEvidenceCorpusBuilder
{
    private readonly ISynthesisCorpusStore _corpusStore;

    public EvidenceCorpusBuilder(ISynthesisCorpusStore corpusStore)
    {
        _corpusStore = corpusStore;
    }

    public async Task<EvidenceCorpus> BuildAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        if (researchRunId == Guid.Empty)
        {
            throw new ArgumentException("Research run id cannot be empty.", nameof(researchRunId));
        }

        var snapshot = await _corpusStore.LoadCorpusAsync(researchRunId, cancellationToken);
        Validate(snapshot, researchRunId);

        var studies = snapshot.Studies
            .OrderBy(study => study.DiscoveredAt)
            .ThenBy(study => study.Pmid)
            .ThenBy(study => study.Pmcid)
            .ThenBy(study => study.Doi)
            .ThenBy(study => study.StudyId)
            .ToArray();
        var studyIds = studies.Select(study => study.StudyId).ToHashSet();
        var sourceMaterials = snapshot.SourceMaterials
            .Where(source => studyIds.Contains(source.StudyId))
            .OrderBy(source => source.StudyId)
            .ThenBy(source => source.Type)
            .ThenBy(source => source.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(source => source.ContentVersion)
            .ThenBy(source => source.SourceMaterialId)
            .ToArray();
        var evidence = snapshot.Evidence
            .OrderBy(item => item.ExtractedAt)
            .ThenBy(item => item.StudyId)
            .ThenBy(item => item.EvidenceId)
            .ToArray();
        var evaluations = snapshot.Evaluations
            .OrderBy(item => item.StudyId)
            .ThenBy(item => item.EvaluationId)
            .ToArray();
        var extractions = snapshot.Extractions
            .OrderBy(item => item.StudyId)
            .ThenBy(item => item.ExtractionId)
            .ToArray();
        var searches = snapshot.Searches
            .OrderBy(item => item.SearchedAt)
            .ThenBy(item => item.LiteratureSearchId)
            .ToArray();
        var outcomeGroups = evidence
            .GroupBy(item => NormalizeOutcome(item.Outcome), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var directions = items.Select(item => item.Direction).Distinct().OrderBy(direction => direction).ToArray();
                return new EvidenceCorpusOutcomeGroup(
                    group.Key,
                    items.Select(item => item.EvidenceId).ToArray(),
                    directions,
                    directions.Contains(EvidenceDirection.Positive)
                        && directions.Contains(EvidenceDirection.Negative));
            })
            .ToArray();

        var sourcesByStudy = sourceMaterials
            .Where(source => source.IsCurrent)
            .GroupBy(source => source.StudyId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var studiesWithEvidence = evidence.Select(item => item.StudyId).Distinct().ToHashSet();
        var fullTextStudies = sourcesByStudy
            .Where(pair => pair.Value.Any(source => source.Type == SourceMaterialType.StructuredFullText && !source.WasTruncated))
            .Select(pair => pair.Key)
            .ToHashSet();
        var abstractStudies = sourcesByStudy.Keys
            .Where(studyId => !fullTextStudies.Contains(studyId))
            .ToHashSet();
        var coverage = new EvidenceCorpusCoverage(
            studies.Length,
            sourcesByStudy.Count,
            fullTextStudies.Count,
            abstractStudies.Count,
            studies.Length - sourcesByStudy.Count,
            studiesWithEvidence.Count,
            studies.Length - studiesWithEvidence.Count,
            evidence.Length,
            evaluations.Count(item => item.Status == EvidenceEvaluationStatus.Completed),
            outcomeGroups.Count(group => group.HasConflict));

        return new EvidenceCorpus(
            researchRunId,
            snapshot,
            studies,
            evidence,
            evaluations,
            extractions,
            sourceMaterials,
            searches,
            outcomeGroups,
            coverage);
    }

    private static void Validate(SynthesisCorpusSnapshot snapshot, Guid expectedRunId)
    {
        if (snapshot.ResearchRunId != expectedRunId)
        {
            throw new ResearchSynthesisValidationException("Evidence corpus did not preserve the authoritative research run id.");
        }

        var studyIds = snapshot.Studies.Select(study => study.StudyId).ToArray();
        if (studyIds.Any(id => id == Guid.Empty) || studyIds.Distinct().Count() != studyIds.Length)
        {
            throw new ResearchSynthesisValidationException("Evidence corpus contains an invalid or duplicate Study snapshot.");
        }

        var sourcesById = snapshot.SourceMaterials.ToDictionary(source => source.SourceMaterialId);
        var extractionsById = snapshot.Extractions.ToDictionary(extraction => extraction.ExtractionId);
        var evidenceIds = new HashSet<Guid>();
        foreach (var extraction in snapshot.Extractions)
        {
            if (extraction.ResearchRunId != expectedRunId || !studyIds.Contains(extraction.StudyId))
            {
                throw new ResearchSynthesisValidationException("Evidence extraction in the corpus is outside the current ResearchRun.");
            }

            if (extraction.SourceMaterialId is { } sourceId)
            {
                if (!sourcesById.TryGetValue(sourceId, out var source) || source.StudyId != extraction.StudyId)
                {
                    throw new ResearchSynthesisValidationException("Evidence extraction references SourceMaterial from another Study.");
                }
            }

            if (extraction.Status == EvidenceExtractionStatus.Completed
                && (extraction.SourceMaterialId is null || !extraction.GroundingValidated))
            {
                throw new ResearchSynthesisValidationException("Completed evidence extraction must have a grounded SourceMaterial lineage.");
            }
        }

        foreach (var evidence in snapshot.Evidence)
        {
            if (evidence.ResearchRunId != expectedRunId || !studyIds.Contains(evidence.StudyId))
            {
                throw new ResearchSynthesisValidationException("Evidence in the corpus is outside the current ResearchRun.");
            }

            if (!evidenceIds.Add(evidence.EvidenceId))
            {
                throw new ResearchSynthesisValidationException("Evidence corpus contains a duplicate Evidence id.");
            }

            if (!extractionsById.TryGetValue(evidence.EvidenceExtractionId, out var extraction)
                || extraction.StudyId != evidence.StudyId
                || extraction.Status != EvidenceExtractionStatus.Completed
                || !extraction.GroundingValidated)
            {
                throw new ResearchSynthesisValidationException("Evidence must reference a grounded completed extraction for the same Study.");
            }

            if (extraction.SourceMaterialId is null || !sourcesById.ContainsKey(extraction.SourceMaterialId.Value))
            {
                throw new ResearchSynthesisValidationException("Evidence must retain the exact SourceMaterial used for extraction.");
            }
        }

        foreach (var evaluation in snapshot.Evaluations)
        {
            if (evaluation.ResearchRunId != expectedRunId || !studyIds.Contains(evaluation.StudyId))
            {
                throw new ResearchSynthesisValidationException("Evidence evaluation in the corpus is outside the current ResearchRun.");
            }

            if (evaluation.EvidenceIds.Any(evidenceId => !evidenceIds.Contains(evidenceId)))
            {
                throw new ResearchSynthesisValidationException("Evidence evaluation references evidence outside the corpus.");
            }
        }

        if (snapshot.Searches.Any(search => search.ResearchRunId != expectedRunId))
        {
            throw new ResearchSynthesisValidationException("Search provenance in the corpus is outside the current ResearchRun.");
        }
    }

    private static string NormalizeOutcome(string outcome)
    {
        return string.Join(' ', outcome.Split(null as char[], StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();
    }
}

namespace MedResearch.Application.Research.Synthesis;

public interface IEvidenceCorpusBuilder
{
    Task<EvidenceCorpus> BuildAsync(Guid researchRunId, CancellationToken cancellationToken);
}

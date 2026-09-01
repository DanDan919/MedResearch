namespace MedResearch.Application.Research.Synthesis;

public interface ISynthesisCorpusStore
{
    Task<SynthesisCorpusSnapshot> LoadCorpusAsync(Guid researchRunId, CancellationToken cancellationToken);
}
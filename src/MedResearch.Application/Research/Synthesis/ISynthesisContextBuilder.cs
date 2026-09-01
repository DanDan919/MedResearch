namespace MedResearch.Application.Research.Synthesis;

public interface ISynthesisContextBuilder
{
    Task<SynthesisContext> BuildAsync(Guid researchRunId, CancellationToken cancellationToken);
}
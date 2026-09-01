namespace MedResearch.Application.Research.Synthesis;

public interface IResearchSynthesizer
{
    Task<ResearchSynthesisResult> SynthesizeAsync(SynthesisContext context, CancellationToken cancellationToken);
}
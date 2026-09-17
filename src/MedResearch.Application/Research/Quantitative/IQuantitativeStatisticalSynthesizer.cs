namespace MedResearch.Application.Research.Quantitative;

public interface IQuantitativeStatisticalSynthesizer
{
    QuantitativeSynthesisReadiness Synthesize(QuantitativeEvidenceReadiness readiness);
}

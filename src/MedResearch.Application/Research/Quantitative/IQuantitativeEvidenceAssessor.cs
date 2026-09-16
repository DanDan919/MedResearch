using MedResearch.Application.Research.Synthesis;

namespace MedResearch.Application.Research.Quantitative;

public interface IQuantitativeEvidenceAssessor
{
    QuantitativeEvidenceReadiness Assess(EvidenceCorpus corpus);
}
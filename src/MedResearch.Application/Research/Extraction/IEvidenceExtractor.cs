namespace MedResearch.Application.Research.Extraction;

public interface IEvidenceExtractor
{
    Task<EvidenceExtractionResult> ExtractAsync(
        EvidenceExtractionStudyContext context,
        CancellationToken cancellationToken);
}

namespace MedResearch.Application.Research.Extraction;

public sealed class EvidenceExtractionOptions
{
    public const string SectionName = "EvidenceExtraction";

    public int MaxStudiesPerRun { get; init; } = 10;

    public int BoundedMaxStudiesPerRun => Math.Clamp(MaxStudiesPerRun, 1, 50);
}

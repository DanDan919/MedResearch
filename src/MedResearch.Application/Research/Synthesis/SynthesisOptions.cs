namespace MedResearch.Application.Research.Synthesis;

public sealed class SynthesisOptions
{
    public const string SectionName = "Synthesis";

    public int MaxStudies { get; init; } = 10;

    public int MaxEvidenceFindings { get; init; } = 40;

    public int MaxClaims { get; init; } = 12;

    public int BoundedMaxStudies => Math.Clamp(MaxStudies, 1, 50);

    public int BoundedMaxEvidenceFindings => Math.Clamp(MaxEvidenceFindings, 1, 100);

    public int BoundedMaxClaims => Math.Clamp(MaxClaims, 1, 25);
}
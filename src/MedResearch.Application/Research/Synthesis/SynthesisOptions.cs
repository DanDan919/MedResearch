namespace MedResearch.Application.Research.Synthesis;

public sealed class SynthesisOptions
{
    public const string SectionName = "Synthesis";
    public const int DefaultMaxClaims = 12;
    public const int MaximumMaxClaims = 25;

    public int MaxStudies { get; init; } = 10;

    public int MaxEvidenceFindings { get; init; } = 40;

    public int MaxClaims { get; init; } = DefaultMaxClaims;

    public int BoundedMaxStudies => Math.Clamp(MaxStudies, 1, 50);

    public int BoundedMaxEvidenceFindings => Math.Clamp(MaxEvidenceFindings, 1, 100);

    public int BoundedMaxClaims => Math.Clamp(MaxClaims, 1, MaximumMaxClaims);
}

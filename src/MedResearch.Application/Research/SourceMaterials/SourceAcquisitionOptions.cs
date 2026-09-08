namespace MedResearch.Application.Research.SourceMaterials;

public sealed class SourceAcquisitionOptions
{
    public const string SectionName = "SourceAcquisition";
    public const int MaximumStudiesPerRun = 50;
    public const int MaximumContentCharactersLimit = 200_000;

    public bool Enabled { get; init; } = true;

    public int MaxStudiesPerRun { get; init; } = 10;

    public int MaxContentCharacters { get; init; } = 30_000;

    public bool PreferStructuredFullText { get; init; } = true;

    public int BoundedMaxStudiesPerRun => Math.Clamp(MaxStudiesPerRun, 1, MaximumStudiesPerRun);

    public int BoundedMaxContentCharacters => Math.Clamp(MaxContentCharacters, 1, MaximumContentCharactersLimit);

    public void Validate()
    {
        if (MaxStudiesPerRun is < 1 or > MaximumStudiesPerRun)
        {
            throw new InvalidOperationException($"SourceAcquisition:MaxStudiesPerRun must be between 1 and {MaximumStudiesPerRun}.");
        }

        if (MaxContentCharacters is < 1 or > MaximumContentCharactersLimit)
        {
            throw new InvalidOperationException($"SourceAcquisition:MaxContentCharacters must be between 1 and {MaximumContentCharactersLimit}.");
        }
    }
}

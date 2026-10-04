namespace MedResearch.Application.Research.Ai;

public sealed class ValidationGuidedLlmRepairOptions
{
    public const string SectionName = "AI:ValidationGuidedRepair";
    public const int MaximumAllowedAttempts = 2;

    public int MaxSemanticRepairAttempts { get; init; } = 1;

    public void Validate()
    {
        if (MaxSemanticRepairAttempts is < 0 or > MaximumAllowedAttempts)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxSemanticRepairAttempts must be between 0 and {MaximumAllowedAttempts}.");
        }
    }
}

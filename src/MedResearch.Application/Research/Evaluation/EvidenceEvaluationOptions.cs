namespace MedResearch.Application.Research.Evaluation;

public sealed class EvidenceEvaluationOptions
{
    public const string SectionName = "EvidenceEvaluation";

    public int MaxStudiesPerRun { get; init; } = 10;

    public int BoundedMaxStudiesPerRun => Math.Clamp(MaxStudiesPerRun, 1, 50);
}

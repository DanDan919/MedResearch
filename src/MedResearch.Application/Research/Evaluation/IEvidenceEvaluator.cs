namespace MedResearch.Application.Research.Evaluation;

public interface IEvidenceEvaluator
{
    Task<EvidenceEvaluationResult> EvaluateAsync(
        EvaluationStudyContext context,
        CancellationToken cancellationToken);
}

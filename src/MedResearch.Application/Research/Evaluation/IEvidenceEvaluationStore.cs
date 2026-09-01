namespace MedResearch.Application.Research.Evaluation;

public interface IEvidenceEvaluationStore
{
    Task<EvidenceEvaluationWorkItemSet> FindStudiesForEvaluationAsync(
        Guid researchRunId,
        string promptVersion,
        int maxStudies,
        CancellationToken cancellationToken);

    Task PersistEvaluationResultAsync(
        EvidenceEvaluationResult result,
        CancellationToken cancellationToken);
}

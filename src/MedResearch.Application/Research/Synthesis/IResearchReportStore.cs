namespace MedResearch.Application.Research.Synthesis;

public interface IResearchReportStore
{
    Task<bool> HasReportAsync(Guid researchRunId, string promptVersion, CancellationToken cancellationToken);

    Task PersistReportAsync(ResearchSynthesisResult result, CancellationToken cancellationToken);

    Task<ResearchReportReadModel?> FindReportAsync(Guid researchRunId, CancellationToken cancellationToken);
}
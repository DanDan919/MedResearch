namespace MedResearch.Application.Research.Processing;

public interface IResearchStageExecutor
{
    Task ExecuteAsync(ResearchStageExecutionContext context, CancellationToken cancellationToken);
}

using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public interface IResearchStageExecutor
{
    Task ExecuteAsync(ResearchRunStatus stage, CancellationToken cancellationToken);
}

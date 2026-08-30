using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public sealed class DeterministicResearchStageExecutor : IResearchStageExecutor
{
    public Task ExecuteAsync(ResearchRunStatus stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

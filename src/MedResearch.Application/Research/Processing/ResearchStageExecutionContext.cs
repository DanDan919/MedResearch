using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public sealed record ResearchStageExecutionContext(
    Guid ResearchRunId,
    ResearchRunStatus Stage,
    string ResearchQuestion,
    string WorkerInstanceId);

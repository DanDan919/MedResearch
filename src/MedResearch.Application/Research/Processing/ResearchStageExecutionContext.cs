using MedResearch.Domain;

namespace MedResearch.Application.Research.Processing;

public sealed record ResearchStageExecutionContext(
    Guid ResearchRunId,
    Guid ResearchQuestionId,
    ResearchRunStatus Stage,
    string ResearchQuestion,
    string WorkerInstanceId);

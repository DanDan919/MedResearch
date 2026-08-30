namespace MedResearch.Domain;

public enum ResearchRunStatus
{
    Queued = 0,
    Planning = 1,
    Searching = 2,
    Extracting = 3,
    Evaluating = 4,
    Synthesizing = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8
}

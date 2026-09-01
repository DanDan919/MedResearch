namespace MedResearch.Application.Research.Processing;

public sealed class ResearchRunLeaseLostException : Exception
{
    public ResearchRunLeaseLostException(Guid researchRunId, string workerInstanceId, long leaseVersion)
        : base($"Research run lease was lost before progress could be persisted. ResearchRunId: {researchRunId}; WorkerInstanceId: {workerInstanceId}; LeaseVersion: {leaseVersion}.")
    {
        ResearchRunId = researchRunId;
        WorkerInstanceId = workerInstanceId;
        LeaseVersion = leaseVersion;
    }

    public Guid ResearchRunId { get; }

    public string WorkerInstanceId { get; }

    public long LeaseVersion { get; }
}
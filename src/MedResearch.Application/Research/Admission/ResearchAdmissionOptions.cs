namespace MedResearch.Application.Research.Admission;

public sealed class ResearchAdmissionOptions
{
    public int OwnerOutstandingLimit { get; init; } = 1;
    public int GlobalOutstandingLimit { get; init; } = 2;
    public int OwnerDailyLimit { get; init; } = 2;
    public int GlobalDailyLimit { get; init; } = 10;
    public bool StopNewAdmissions { get; init; }

    public void Validate()
    {
        if (OwnerOutstandingLimit is < 1 or > 10_000 ||
            GlobalOutstandingLimit is < 1 or > 10_000 ||
            OwnerDailyLimit is < 1 or > 10_000 ||
            GlobalDailyLimit is < 1 or > 10_000)
            throw new InvalidOperationException("ResearchAdmission limits must be between 1 and 10000.");
        if (OwnerOutstandingLimit > GlobalOutstandingLimit || OwnerDailyLimit > GlobalDailyLimit)
            throw new InvalidOperationException("ResearchAdmission owner limits must not exceed global limits.");
    }
}

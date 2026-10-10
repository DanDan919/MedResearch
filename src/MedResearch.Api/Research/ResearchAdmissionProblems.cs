using MedResearch.Application.Research.Admission;

namespace MedResearch.Api.Research;

internal static class ResearchAdmissionProblems
{
    public static (int Status, string Title, string Code) Describe(ResearchAdmissionFailure failure) => failure switch
    {
        ResearchAdmissionFailure.InvalidKey => (400, "A non-empty UUID Idempotency-Key is required", "admission-invalid-key"),
        ResearchAdmissionFailure.IdempotencyConflict => (409, "This submission key was already used for a different question", "admission-idempotency-conflict"),
        ResearchAdmissionFailure.OwnerOutstanding => (429, "Your outstanding research limit has been reached", "admission-owner-outstanding"),
        ResearchAdmissionFailure.GlobalOutstanding => (429, "Research capacity is currently full", "admission-global-outstanding"),
        ResearchAdmissionFailure.OwnerDaily => (429, "Your daily research limit has been reached", "admission-owner-daily"),
        ResearchAdmissionFailure.GlobalDaily => (429, "The daily research capacity has been reached", "admission-global-daily"),
        ResearchAdmissionFailure.Stopped => (503, "New research submissions are temporarily paused", "admission-stopped"),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };
}

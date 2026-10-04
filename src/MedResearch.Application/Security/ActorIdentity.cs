namespace MedResearch.Application.Security;

using MedResearch.Domain;

public static class ActorIdentity
{
    public const int MaximumSubjectLength = ResearchOwnership.MaximumSubjectLength;
    public const string LegacyUnownedSubjectId = ResearchOwnership.LegacyUnownedSubjectId;
    public const string SystemSubjectId = "system-worker";

    public static string NormalizeSubject(string subject)
    {
        return ResearchOwnership.NormalizeSubject(subject);
    }
}

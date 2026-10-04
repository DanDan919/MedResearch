namespace MedResearch.Domain;

public static class ResearchOwnership
{
    public const int MaximumSubjectLength = 200;
    public const string LegacyUnownedSubjectId = "legacy-unowned";

    public static string NormalizeSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Owner subject is required.", nameof(subject));
        }

        var normalized = subject.Trim();
        if (normalized.Length > MaximumSubjectLength)
        {
            throw new ArgumentException($"Owner subject cannot exceed {MaximumSubjectLength} characters.", nameof(subject));
        }

        return normalized;
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace MedResearch.Domain.QuickResearch;

public sealed record QuickAnswerCitation(Guid SourceId, string Excerpt);
public sealed record QuickAnswerFinding(string Text, QuickAnswerCitation[] Citations);
public sealed record QuickAnswerContradiction(string Text, QuickAnswerCitation[] Citations);
public sealed record QuickAnswer(Guid QuickRunId, string ShortAnswer, QuickAnswerFinding[] Findings,
    QuickAnswerContradiction[] Contradictions, string[] Limitations, string Uncertainty, Guid[] SourceReferences)
{
    public void ValidateShape(Guid runId)
    {
        if (runId == Guid.Empty || QuickRunId != runId || !Prose(ShortAnswer, 600)
            || Findings is null || Findings.Length > 6 || Contradictions is null || Contradictions.Length > 3
            || Limitations is null || Limitations.Length is < 1 or > 8 || Limitations.Any(x => !Prose(x, 500))
            || Uncertainty is not ("Limited" or "VeryLimited" or "Insufficient")
            || SourceReferences is null || SourceReferences.Length > 5 || SourceReferences.Any(x => x == Guid.Empty)
            || SourceReferences.Distinct().Count() != SourceReferences.Length)
            throw new ArgumentException("Invalid Quick answer shape.");
        foreach (var finding in Findings)
            if (finding is null || !Prose(finding.Text, 600) || !Citations(finding.Citations, 1))
                throw new ArgumentException("Invalid Quick finding.");
        foreach (var contradiction in Contradictions)
            if (contradiction is null || !Prose(contradiction.Text, 600) || !Citations(contradiction.Citations, 2))
                throw new ArgumentException("Quick disagreement requires different cited sources.");
        var cited = Findings.SelectMany(x => x.Citations).Concat(Contradictions.SelectMany(x => x.Citations))
            .Select(x => x.SourceId).Distinct().Order().ToArray();
        if (!cited.SequenceEqual(SourceReferences.Order())
            || Uncertainty == "Insufficient" && (Findings.Length != 0 || Contradictions.Length != 0 || SourceReferences.Length != 0)
            || Uncertainty != "Insufficient" && (Findings.Length == 0 || ShortAnswer != Findings[0].Text))
            throw new ArgumentException("Incoherent Quick answer support.");
    }

    private static bool Citations(QuickAnswerCitation[]? citations, int minimum) => citations is not null
        && citations.Length >= minimum && citations.Length <= 5
        && citations.Select(x => x?.SourceId).Distinct().Count() == citations.Length
        && citations.All(x => x is not null && x.SourceId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Excerpt)
            && x.Excerpt.Length is >= 20 and <= 800 && !QuickAnswerText.IsInstruction(x.Excerpt));

    private static bool Prose(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max
        && !QuickAnswerText.HasNumericDetail(text) && !QuickAnswerText.IsInstruction(text) && !QuickAnswerText.IsUnsafeClaim(text);
}

public static class QuickAnswerText
{
    public const string NormalizationVersion = "quick-excerpt-v1";

    // Case/punctuation/digits remain significant; only NFC and whitespace are normalized.
    public static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormC))
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
            }
            else result.Append(character);
        return result.ToString().Trim();
    }

    public static bool HasNumericDetail(string text) => text.Any(char.IsNumber)
        || Regex.IsMatch(text, @"[%=<>]|\b(one|two|three|four|five|six|seven|eight|nine|ten|hundred|thousand|twice|double|triple|percent|один|одна|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять|сто|тысяч\w*|процент\w*|вдвое|втрое)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsInstruction(string text) => Regex.IsMatch(text,
        @"ignore\s+(all\s+)?(previous|prior)\s+instructions|system\s+prompt|developer\s+message|you\s+are\s+chatgpt|игнорир\w*\s+(предыдущ|инструкц)|системн\w*\s+промпт",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsUnsafeClaim(string text) => Regex.IsMatch(text,
        @"\b(you\s+(must|should|have)|take\s+\w+|start\s+taking|stop\s+taking|guaranteed|cures)\b|exhaustive\s+review|complete\s+systematic\s+review|\b(принимайте|отмените|увеличьте|вылечит)\b|у\s+вас\s+диагноз|полн\w*\s+систематическ\w*\s+обзор",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public enum QuickAnswerOutcome { Started = 0, Succeeded = 1, InsufficientEvidence = 2, Failed = 3, OutcomeUnknown = 4 }

public sealed record QuickAnswerGeneration(Guid Id, Guid QuickRunId, string PromptVersion, string InputFingerprint,
    int AttemptCount, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, QuickAnswerOutcome Outcome,
    string? FailureCategory, QuickAnswer? Answer)
{
    public void Validate(Guid runId)
    {
        if (Id == Guid.Empty || QuickRunId != runId || PromptVersion != "quick-answer-v1"
            || InputFingerprint is null || !Regex.IsMatch(InputFingerprint, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)
            || AttemptCount is < 0 or > 1 || !Enum.IsDefined(Outcome)
            || (Outcome == QuickAnswerOutcome.Started) != (FinishedAt is null) || FinishedAt < StartedAt
            || Outcome is QuickAnswerOutcome.Started or QuickAnswerOutcome.Succeeded or QuickAnswerOutcome.OutcomeUnknown && AttemptCount != 1
            || (Outcome is QuickAnswerOutcome.Succeeded or QuickAnswerOutcome.InsufficientEvidence) != (Answer is not null)
            || (Outcome is QuickAnswerOutcome.Failed or QuickAnswerOutcome.OutcomeUnknown) != (FailureCategory is not null)
            || FailureCategory is not null && FailureCategory is not ("InvalidAnswer" or "ProviderFailure" or "OutcomeUnknown" or "GenerationDisabled" or "InputTooLarge" or "RetrievalFailed"))
            throw new ArgumentException("Invalid Quick generation state.");
        Answer?.ValidateShape(runId);
        if (Answer is not null && (Outcome == QuickAnswerOutcome.InsufficientEvidence) != (Answer.Uncertainty == "Insufficient"))
            throw new ArgumentException("Incoherent Quick answer outcome.");
    }
}

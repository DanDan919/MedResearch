using System.Text.Json.Serialization;
using MedResearch.Domain.QuickResearch;

namespace MedResearch.Application.QuickResearch;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuickAnswerDraft(string ShortAnswer, QuickFindingDraft[] Findings,
    QuickContradictionDraft[] Contradictions, string[] Limitations, string Uncertainty, Guid[] SourceReferences);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuickFindingDraft(string Text, QuickCitationDraft[] Citations);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuickContradictionDraft(string Text, QuickCitationDraft[] Citations);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuickCitationDraft(Guid SourceId, string Excerpt);

public sealed class QuickAnswerOptions
{
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxSourceCharacters { get; set; } = 6000;
    public int MaxPromptCharacters { get; set; } = 40000;
    public int MaxOutputTokens { get; set; } = 1200;
    public int MaxResponseBytes { get; set; } = 64000;

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 60 || MaxSourceCharacters is < 100 or > 6000
            || MaxPromptCharacters is < 2000 or > 40000 || MaxOutputTokens is < 256 or > 2000
            || MaxResponseBytes is < 4096 or > 64000)
            throw new InvalidOperationException("Invalid bounded Quick answer settings.");
    }
}

public interface IQuickAnswerStore
{
    Task<bool> ReserveAnswerAsync(QuickResearchClaim claim, QuickAnswerGeneration generation, CancellationToken token);
    Task<bool> SettleAnswerAsync(QuickResearchClaim claim, QuickAnswerGeneration generation, CancellationToken token);
}

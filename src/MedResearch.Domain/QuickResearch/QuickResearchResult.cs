namespace MedResearch.Domain.QuickResearch;

public sealed record QuickResearchSource(Guid Id, Guid QuickRunId, string Title, string? Url, bool IsSynthetic);
public sealed record QuickResearchStatement(string Text, Guid[] SourceIds);
public sealed record QuickResearchResult(Guid QuickRunId, bool IsSynthetic, string Summary,
    QuickResearchStatement[] Facts, QuickResearchStatement[] Disagreements, string[] Limitations, QuickResearchSource[] Sources)
{
    public void ValidateQ0(Guid runId)
    {
        if (runId == Guid.Empty || QuickRunId != runId || !IsSynthetic || !Bounded(Summary, 2000)
            || Facts is null || Disagreements is null || Limitations is null || Sources is null
            || Facts.Length > 10 || Disagreements.Length > 10 || Limitations.Length > 10 || Sources.Length > 10
            || Limitations.Any(text => !Bounded(text, 1000)))
            throw new ArgumentException("Invalid synthetic Quick research result.");
        var ids = new HashSet<Guid>();
        foreach (var source in Sources)
        {
            if (source is null || source.Id == Guid.Empty || !ids.Add(source.Id) || source.QuickRunId != runId
                || !source.IsSynthetic || !Bounded(source.Title, 300)
                || (source.Url is not null && (source.Url.Length > 2048 || !Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("https" or "http"))))
                throw new ArgumentException("Invalid Quick research source lineage.");
        }
        foreach (var statement in Facts.Concat(Disagreements))
        {
            if (statement is null || !Bounded(statement.Text, 1000) || statement.SourceIds is null
                || statement.SourceIds.Length is < 1 or > 10 || statement.SourceIds.Distinct().Count() != statement.SourceIds.Length
                || statement.SourceIds.Any(id => !ids.Contains(id)))
                throw new ArgumentException("Quick statements must cite sources from the same QuickRun.");
        }
    }

    private static bool Bounded(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum;

    public static QuickResearchResult CreateSynthetic(Guid runId) => new(runId, true,
        "Q0 demonstration completed. No medical conclusion was generated.", [], [],
        ["Synthetic result only: no literature, external HTTP or LLM was used.", "This is not medical advice or a verified answer to the question."], []);
}

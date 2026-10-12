using MedResearch.Domain.QuickResearch;

namespace MedResearch.Application.QuickResearch;

public static class QuickAnswerValidator
{
    public static string InsufficientText(string language) => language == "ru"
        ? "Доступных материалов недостаточно для обоснованного ответа."
        : "The available material is insufficient for a supported answer.";

    public static QuickAnswer ValidateDraft(QuickAnswerDraft draft, QuickRetrievalSnapshot retrieval, Guid[] allowedSources)
    {
        if (draft is null || draft.Findings is null || draft.Contradictions is null || draft.Limitations is null
            || draft.Findings.Any(x => x is null || x.Citations is null || x.Citations.Any(c => c is null))
            || draft.Contradictions.Any(x => x is null || x.Citations is null || x.Citations.Any(c => c is null))
            || draft.Limitations.Length > 4)
            throw new ArgumentException("Invalid Quick answer draft.");
        var answer = new QuickAnswer(retrieval.QuickRunId, draft.ShortAnswer,
            draft.Findings.Select(f => new QuickAnswerFinding(f.Text, f.Citations.Select(c => new QuickAnswerCitation(c.SourceId, c.Excerpt)).ToArray())).ToArray(),
            draft.Contradictions.Select(f => new QuickAnswerContradiction(f.Text, f.Citations.Select(c => new QuickAnswerCitation(c.SourceId, c.Excerpt)).ToArray())).ToArray(),
            [.. draft.Limitations, .. MandatoryLimitations(retrieval)], draft.Uncertainty, draft.SourceReferences);
        ValidateStored(answer, retrieval);
        if (answer.SourceReferences.Any(id => !allowedSources.Contains(id)))
            throw new ArgumentException("Quick answer cites unavailable prompt material.");
        return answer;
    }

    public static void ValidateStored(QuickAnswer answer, QuickRetrievalSnapshot retrieval)
    {
        retrieval.Validate(answer.QuickRunId);
        if (!retrieval.IsFinal || retrieval.Outcome == QuickRetrievalOutcome.Failed)
            throw new ArgumentException("Operational retrieval failure cannot support a medical answer.");
        answer.ValidateShape(retrieval.QuickRunId);
        if (MandatoryLimitations(retrieval).Any(required => !answer.Limitations.Contains(required, StringComparer.Ordinal)))
            throw new ArgumentException("Quick answer lost its mandatory safety limitations.");
        if (answer.Uncertainty == "Insufficient" && answer.ShortAnswer != InsufficientText(retrieval.Plan.Language))
            throw new ArgumentException("Insufficient Quick answer must not invent a conclusion.");
        var sources = retrieval.Sources.ToDictionary(s => s.Id);
        foreach (var citation in answer.Findings.SelectMany(f => f.Citations).Concat(answer.Contradictions.SelectMany(c => c.Citations)))
        {
            if (!sources.TryGetValue(citation.SourceId, out var source) || source.QuickRunId != answer.QuickRunId
                || string.IsNullOrWhiteSpace(source.Abstract))
                throw new ArgumentException("Quick citation lacks current-run abstract material.");
            var text = QuickAnswerText.Normalize(source.Abstract);
            var excerpt = QuickAnswerText.Normalize(citation.Excerpt);
            var first = text.IndexOf(excerpt, StringComparison.Ordinal);
            if (excerpt.Length < 20 || first < 0 || text.IndexOf(excerpt, first + 1, StringComparison.Ordinal) >= 0)
                throw new ArgumentException("Quick excerpt does not have a unique exact source match.");
        }
    }

    public static QuickAnswer Insufficient(QuickRetrievalSnapshot retrieval) => new(retrieval.QuickRunId,
        InsufficientText(retrieval.Plan.Language), [], [], MandatoryLimitations(retrieval), "Insufficient", []);

    private static string[] MandatoryLimitations(QuickRetrievalSnapshot retrieval)
    {
        string[] limits = retrieval.Plan.Language == "ru"
        ? ["Выводы ограничены доступными аннотациями; полнота обзора не установлена.",
            "Ссылки и точные цитаты не доказывают правильность медицинской интерпретации.",
            "Это информация об исследованиях, а не диагноз или индивидуальная рекомендация лечения."]
        : ["Conclusions are limited to available abstracts; review completeness is not established.",
            "References and exact excerpts do not prove that the medical interpretation is correct.",
            "This is research information, not a diagnosis or individual treatment advice."];
        return retrieval.Outcome == QuickRetrievalOutcome.Partial
            ? [.. limits, retrieval.Plan.Language == "ru" ? "Некоторые источники были недоступны; поиск имеет неполное покрытие."
                : "Some providers were unavailable; search coverage is incomplete."]
            : limits;
    }
}

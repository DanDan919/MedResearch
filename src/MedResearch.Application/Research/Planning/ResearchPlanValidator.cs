using System.Text.RegularExpressions;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain;

namespace MedResearch.Application.Research.Planning;

public static class ResearchPlanValidator
{
    public const int MaximumSearchQueryCount = 5;
    public const int MaximumSearchQueryLength = 300;
    public const int MaximumOptionalTextLength = 500;
    public const int MaximumListItemCount = 10;

    private static readonly HashSet<string> AllowedStudyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "randomized controlled trial",
        "controlled trial",
        "cohort study",
        "case-control study",
        "cross-sectional study",
        "systematic review",
        "meta-analysis",
        "observational study",
        "experimental study",
        "qualitative study",
        "review",
        "other"
    };

    public static ResearchPlan CreateValidatedPlan(
        Guid id,
        Guid researchRunId,
        Guid researchQuestionId,
        string authoritativeQuestion,
        ResearchPlanDraft draft,
        StructuredLlmProviderMetadata metadata,
        string promptVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritativeQuestion);

        var normalizedAuthoritativeQuestion = Normalize(authoritativeQuestion);
        var normalizedDraftQuestion = Normalize(draft.OriginalQuestion);

        if (!string.Equals(normalizedAuthoritativeQuestion, normalizedDraftQuestion, StringComparison.Ordinal))
        {
            throw new ResearchPlanValidationException("Planner output changed the original research question.");
        }

        var searchQueries = ValidateSearchQueries(draft.SearchQueries);
        var preferredStudyTypes = ValidatePreferredStudyTypes(draft.PreferredStudyTypes);

        return new ResearchPlan(
            id,
            researchRunId,
            researchQuestionId,
            authoritativeQuestion,
            ValidateOptionalText(draft.Population, nameof(draft.Population)),
            ValidateOptionalText(draft.ExposureOrIntervention, nameof(draft.ExposureOrIntervention)),
            ValidateOptionalText(draft.Comparator, nameof(draft.Comparator)),
            ValidateStringList(draft.Outcomes, nameof(draft.Outcomes)),
            preferredStudyTypes,
            searchQueries,
            ValidateStringList(draft.ExclusionHints, nameof(draft.ExclusionHints)),
            metadata.Provider,
            metadata.Model,
            promptVersion,
            metadata.GeneratedAt);
    }

    private static string[] ValidateSearchQueries(IReadOnlyCollection<string>? queries)
    {
        if (queries is null || queries.Count == 0)
        {
            throw new ResearchPlanValidationException("Planner output did not include any search queries.");
        }

        if (queries.Count > MaximumSearchQueryCount)
        {
            throw new ResearchPlanValidationException($"Planner output included more than {MaximumSearchQueryCount} search queries.");
        }

        var normalized = new List<string>();
        foreach (var query in queries)
        {
            var value = Normalize(query);
            if (value is null)
            {
                throw new ResearchPlanValidationException("Planner output included a blank search query.");
            }

            if (value.Length > MaximumSearchQueryLength)
            {
                throw new ResearchPlanValidationException($"Planner output included a search query longer than {MaximumSearchQueryLength} characters.");
            }

            if (LooksLikeStableStudyIdentifier(value))
            {
                throw new ResearchPlanValidationException("Planner output included a prohibited study identifier in a search query.");
            }

            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(value);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ResearchPlanValidationException("Planner output did not include any usable search queries.");
        }

        return normalized.ToArray();
    }

    private static string[] ValidatePreferredStudyTypes(IReadOnlyCollection<string>? values)
    {
        var studyTypes = ValidateStringList(values, "PreferredStudyTypes");

        foreach (var studyType in studyTypes)
        {
            if (!AllowedStudyTypes.Contains(studyType))
            {
                throw new ResearchPlanValidationException($"Planner output included unsupported study type '{studyType}'.");
            }
        }

        return studyTypes;
    }

    private static string? ValidateOptionalText(string? value, string fieldName)
    {
        var normalized = Normalize(value);
        if (normalized is not null && normalized.Length > MaximumOptionalTextLength)
        {
            throw new ResearchPlanValidationException($"Planner output field {fieldName} exceeded {MaximumOptionalTextLength} characters.");
        }

        return normalized;
    }

    private static string[] ValidateStringList(IReadOnlyCollection<string>? values, string fieldName)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        if (values.Count > MaximumListItemCount)
        {
            throw new ResearchPlanValidationException($"Planner output field {fieldName} included too many items.");
        }

        var normalized = new List<string>();
        foreach (var value in values)
        {
            var item = Normalize(value);
            if (item is null)
            {
                continue;
            }

            if (item.Length > MaximumOptionalTextLength)
            {
                throw new ResearchPlanValidationException($"Planner output field {fieldName} included an item longer than {MaximumOptionalTextLength} characters.");
            }

            if (!normalized.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(item);
            }
        }

        return normalized.ToArray();
    }

    private static bool LooksLikeStableStudyIdentifier(string value)
    {
        return value.Contains("PMID", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DOI", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(value, @"\b10\.\d{4,9}/\S+", RegexOptions.IgnoreCase);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(null as char[], StringSplitOptions.RemoveEmptyEntries));
    }
}


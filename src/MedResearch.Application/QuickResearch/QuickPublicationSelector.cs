using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MedResearch.Domain.QuickResearch;

namespace MedResearch.Application.QuickResearch;

public sealed class QuickPublicationSelector : IQuickPublicationSelector
{
    public (QuickPublication[] Sources, int RejectedCount) Select(Guid runId, QuickQueryPlan plan, QuickCandidate[] candidates)
    {
        if (candidates.Length > 32) throw new ArgumentException("Quick candidate budget exceeded.");
        var groups = new List<List<QuickCandidate>>();
        foreach (var candidate in candidates)
        {
            var overlapping = groups.Where(g => g.Any(c => Keys(c).Intersect(Keys(candidate), StringComparer.Ordinal).Any())).ToArray();
            var group = new List<QuickCandidate> { candidate };
            foreach (var existing in overlapping) { group.AddRange(existing); groups.Remove(existing); }
            groups.Add(group);
        }
        var accepted = new List<(QuickPublication Source, int Score, string Key)>();
        var rejected = 0;
        foreach (var group in groups)
        {
            if (group.Select(c => c.Pmid).Where(v => v is not null).Distinct().Count() > 1
                || group.Select(c => c.Pmcid).Where(v => v is not null).Distinct().Count() > 1
                || group.Select(c => c.Doi).Where(v => v is not null).Distinct().Count() > 1
                || group.Select(c => c.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            { rejected += group.Count; continue; }
            var conflicts = new List<string>();
            string? Agree(string field, IEnumerable<string?> values)
            {
                var nonempty = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Order(StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (nonempty.Length > 1) { conflicts.Add(field); return null; }
                return nonempty.SingleOrDefault();
            }
            string[] AgreeList(string field, IEnumerable<string[]> lists)
            {
                var value = Agree(field, lists.Where(a => a.Length > 0).Select(a => string.Join('\n', a)));
                return value?.Split('\n') ?? [];
            }
            var title = group.Select(c => c.Title).OrderBy(t => t, StringComparer.Ordinal).First();
            var abstractText = Agree("Abstract", group.Select(c => c.Abstract));
            var titleLower = title.ToLowerInvariant();
            var abstractLower = abstractText?.ToLowerInvariant() ?? "";
            if (plan.Terms.Any(term => !term.MatchTerms.Any(t => Contains(titleLower, t) || Contains(abstractLower, t))))
            { rejected += group.Count; continue; }
            var text = titleLower + " " + abstractLower;
            if (plan.Population is not null)
            {
                if (!plan.PopulationTerms.Any(t => Contains(text, t)) || ConflictingPopulation(plan.Population, text))
                { rejected += group.Count; continue; }
            }
            var pmid = group.Select(c => c.Pmid).FirstOrDefault(v => v is not null);
            var pmcid = group.Select(c => c.Pmcid).FirstOrDefault(v => v is not null);
            var doi = group.Select(c => c.Doi).FirstOrDefault(v => v is not null);
            var key = string.Join('|', Keys(group.First()).Concat(group.SelectMany(Keys)).Distinct().Order(StringComparer.Ordinal));
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:D}:{key}"))[..16]);
            var dates = group.Select(c => c.PublicationDate).Where(d => d.HasValue).Distinct().ToArray();
            var years = group.Select(c => c.PublicationYear ?? c.PublicationDate?.Year).Where(d => d.HasValue).Distinct().ToArray();
            if (dates.Length > 1 || dates.Length > 0 && years.Length > 1) conflicts.Add("PublicationDate");
            if (years.Length > 1) conflicts.Add("PublicationYear");
            var source = new QuickPublication(id, runId, pmid, pmcid, doi, title, abstractText,
                Agree("Journal", group.Select(c => c.Journal)), dates.Length == 1 && years.Length == 1 ? dates[0] : null, years.Length == 1 ? years[0] : null,
                AgreeList("Authors", group.Select(c => c.Authors)), AgreeList("PublicationTypes", group.Select(c => c.PublicationTypes)),
                group.Select(c => c.Discovery).Distinct().OrderBy(d => d.Provider, StringComparer.Ordinal).ThenBy(d => d.ProviderRecordId, StringComparer.Ordinal)
                    .ThenBy(d => d.AttemptId).ToArray(), conflicts.Order(StringComparer.Ordinal).ToArray(), plan.Population is null ? "Unspecified" : "MatchedText");
            accepted.Add((source, plan.Terms.Count(t => t.MatchTerms.Any(m => Contains(titleLower, m))), key));
        }
        return (accepted.OrderByDescending(a => a.Score).ThenBy(a => a.Key, StringComparer.Ordinal).Take(5).Select(a => a.Source).ToArray(), rejected);
    }

    private static IEnumerable<string> Keys(QuickCandidate c)
    {
        if (c.Pmid is not null) yield return "pmid:" + c.Pmid;
        if (c.Pmcid is not null) yield return "pmcid:" + c.Pmcid;
        if (c.Doi is not null) yield return "doi:" + c.Doi;
    }
    private static bool Contains(string text, string term) => Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);
    private static bool ConflictingPopulation(string population, string text)
    {
        var children = new[] { "child", "children", "pediatric", "paediatric", "infant", "infants" }.Any(t => Contains(text, t));
        var adults = new[] { "adult", "adults" }.Any(t => Contains(text, t));
        return population switch { "adults" => children, "children" => adults, "elderly" => children, _ => false };
    }
}

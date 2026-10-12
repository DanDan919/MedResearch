using System.Text.RegularExpressions;
using MedResearch.Domain.QuickResearch;

namespace MedResearch.Application.QuickResearch;

public static partial class QuickQueryPreparation
{
    private sealed record Concept(string Key, string[] English, string[] Aliases, bool Population = false);
    private static readonly Concept[] Concepts =
    [
        new("sleep", ["sleep"], ["sleep", "сон", "сна", "сну", "сном"]),
        new("insomnia", ["insomnia"], ["insomnia", "бессонница", "бессоннице", "бессонницы", "бессонницу"]),
        new("magnesium", ["magnesium"], ["magnesium", "магний", "магния", "магнием"]),
        new("melatonin", ["melatonin"], ["melatonin", "мелатонин", "мелатонина"]),
        new("electronic cigarettes", ["electronic cigarettes", "e-cigarettes", "vaping"], ["electronic cigarettes", "e cigarettes", "vaping", "электронные сигареты", "электронных сигарет", "вейпы", "вейпов"]),
        new("creatine", ["creatine"], ["creatine", "креатин", "креатина"]),
        new("memory", ["memory"], ["memory", "память", "памяти"]),
        new("anxiety", ["anxiety"], ["anxiety", "тревога", "тревогу", "тревоги", "тревожность", "тревожности"]),
        new("sleep deprivation", ["sleep deprivation", "poor sleep"], ["sleep deprivation", "poor sleep", "плохой сон", "плохого сна", "недосып", "недосыпание", "недостаток сна"]),
        new("antidepressants", ["antidepressants", "antidepressant"], ["antidepressants", "antidepressant", "антидепрессанты", "антидепрессантов"]),
        new("stimulants", ["stimulants", "stimulant"], ["stimulants", "stimulant", "стимуляторы", "стимуляторов"]),
        new("long term", ["long term", "long-term"], ["long term", "долгосрочные", "долгосрочных"]),
        new("vitamin d", ["vitamin d"], ["vitamin d", "витамин d", "витамина d", "витамин д", "витамина д"]),
        new("aspirin", ["aspirin", "acetylsalicylic acid"], ["aspirin", "acetylsalicylic acid", "аспирин", "аспирина"]),
        new("headache", ["headache", "headaches"], ["headache", "headaches", "головная боль", "головной боли"]),
        new("migraine", ["migraine"], ["migraine", "мигрень", "мигрени"]),
        new("asthma", ["asthma"], ["asthma", "астма", "астме", "астмы"]),
        new("diabetes", ["diabetes"], ["diabetes", "диабет", "диабете", "диабета"]),
        new("metformin", ["metformin"], ["metformin", "метформин", "метформина"]),
        new("hypertension", ["hypertension"], ["hypertension", "гипертония", "гипертонии"]),
        new("blood pressure", ["blood pressure"], ["blood pressure", "артериальное давление", "артериального давления"]),
        new("depression", ["depression"], ["depression", "депрессия", "депрессии"]),
        new("exercise", ["exercise"], ["exercise", "упражнения", "упражнений", "физическая активность", "физической активности"]),
        new("bleeding", ["bleeding"], ["bleeding", "кровотечение", "кровотечения"]),
        new("adults", ["adult", "adults"], ["adult", "adults", "взрослые", "взрослых", "взрослым"], true),
        new("children", ["children", "child", "pediatric", "paediatric"], ["children", "child", "pediatric", "paediatric", "дети", "детей", "детям", "детских"], true),
        new("pregnancy", ["pregnancy", "pregnant"], ["pregnancy", "pregnant", "беременность", "беременности", "беременных"], true),
        new("elderly", ["elderly", "older adults"], ["elderly", "older adults", "пожилых", "пожилым"], true)
    ];
    private static readonly HashSet<string> Stops = new(("does do is are can could should will may i me my we the a an of to for in with and on at how what why whether "
        + "help helps helpful benefit benefits effect effects effective effectiveness treatment treat reduce improve improves cause causes risk risks affect affects increase increases harmful work works actually "
        + "ли как какой какая какие что это мне мой моя у в во на при для и от с со помогает помочь помогает ли влияние влияет эффект лечение лечить "
        + "польза полезен полезно эффективен эффективна эффективность снижает улучшает вызывает риск риски насколько вредны вреден работают работает действительно может могут усиливать повышать влияет влияют последствия эффекты").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    private static readonly HashSet<string> Unsupported = new(["not", "no", "without", "except", "не", "нет", "без", "кроме", "versus", "vs"], StringComparer.Ordinal);

    public static QuickQueryPlan Prepare(string question)
    {
        var normalized = question.Normalize().ToLowerInvariant().Replace('ё', 'е');
        var language = normalized.Any(c => c is >= 'а' and <= 'я') ? "ru" : "en";
        var words = Words().Matches(normalized).Select(m => m.Value).ToArray();
        QuickQueryPlan UnsupportedPlan() => new("quick-query-v1", language, [], [], null, []);
        if (words.Length is < 1 or > 60 || words.Any(Unsupported.Contains)) return UnsupportedPlan();
        var topics = new Dictionary<string, QuickQueryTerm>(StringComparer.Ordinal);
        var populations = new Dictionary<string, Concept>(StringComparer.Ordinal);
        for (var index = 0; index < words.Length;)
        {
            Concept? matched = null;
            var consumed = 0;
            foreach (var concept in Concepts)
                foreach (var alias in concept.Aliases)
                {
                    var tokens = alias.Split(' ');
                    if (tokens.Length > consumed && words.Skip(index).Take(tokens.Length).SequenceEqual(tokens))
                    { matched = concept; consumed = tokens.Length; }
                }
            if (matched is not null)
            {
                if (matched.Population) populations.TryAdd(matched.Key, matched);
                else topics.TryAdd(matched.Key, new(Clause(matched.English), matched.English));
                index += consumed;
                continue;
            }
            var word = words[index++];
            if (Stops.Contains(word)) continue;
            // Unknown Cyrillic cannot be silently dropped or guessed into English.
            if (!AsciiTerm().IsMatch(word) || word.Length > 40) return UnsupportedPlan();
            topics.TryAdd(word, new(Clause([word]), [word]));
        }
        if (topics.Count is < 1 or > 8 || populations.Count > 1) return UnsupportedPlan();
        var terms = topics.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value).ToArray();
        var population = populations.Values.SingleOrDefault();
        var parts = terms.Select(t => t.QueryText).ToList();
        if (population is not null) parts.Add(Clause(population.English));
        var query = string.Join(" AND ", parts);
        return query.Length > 512 ? UnsupportedPlan() : new("quick-query-v1", language, [query], terms, population?.Key, population?.English ?? []);
    }

    private static string Clause(string[] terms) => terms.Length == 1 ? $"\"{terms[0]}\"" : $"({string.Join(" OR ", terms.Select(t => $"\"{t}\""))})";
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)] private static partial Regex Words();
    [GeneratedRegex("^[a-z0-9]+$", RegexOptions.CultureInvariant)] private static partial Regex AsciiTerm();
}

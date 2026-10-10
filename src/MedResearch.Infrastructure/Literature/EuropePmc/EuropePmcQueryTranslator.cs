using MedResearch.Application.Research.Literature;
using MedResearch.Domain;

namespace MedResearch.Infrastructure.Literature.EuropePmc;

internal static class EuropePmcQueryTranslator
{
    private const int MaximumQueryLength = 2000;
    private const int MaximumNestingDepth = 32;
    public static string Translate(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > MaximumQueryLength) throw Unsupported();
        if (!HasFieldSyntax(query)) return query;
        return new Parser(query).Parse();
    }

    private static bool HasFieldSyntax(string query)
    {
        var quoted = false;
        var fieldSyntax = false;
        for (var index = 0; index < query.Length; index++)
        {
            if (query[index] == '\\' && quoted) { index++; continue; }
            if (query[index] == '"') quoted = !quoted;
            else if (!quoted && query[index] is '[' or ']') fieldSyntax = true;
        }
        if (quoted && (query.Contains('[') || query.Contains(']'))) throw Unsupported();
        return fieldSyntax;
    }

    private static ScientificLiteratureSourceException Unsupported() => new(
        "Europe PMC cannot safely execute this planned query. Supported PubMed fields are Title/Abstract (tiab) and Title (ti); other fields or malformed expressions require a new plan.",
        LiteratureProviderFailureCategory.ProviderProtocolError);

    // Parse fielded operands before rewriting. Removing suffixes with a regex would lose scope.
    private sealed class Parser(string query)
    {
        private readonly List<Token> _tokens = Tokenize(query);
        private int _position;

        public string Parse()
        {
            var result = Expression(0);
            if (_position != _tokens.Count || result.Length > MaximumQueryLength) throw Unsupported();
            return result;
        }

        private string Expression(int depth)
        {
            if (depth > MaximumNestingDepth) throw Unsupported();
            var result = Operand(depth);
            string? previousOperation = null;
            while (_position < _tokens.Count && Peek.Kind != Kind.Close)
            {
                var operation = "AND";
                if (Peek.Kind == Kind.Operator) operation = _tokens[_position++].Text;
                // PubMed evaluates left-to-right; do not delegate mixed precedence to another engine.
                if (previousOperation is not null && previousOperation != operation) result = $"({result})";
                result += $" {operation} {Operand(depth)}";
                previousOperation = operation;
            }
            return result;
        }

        private string Operand(int depth)
        {
            if (_position >= _tokens.Count) throw Unsupported();
            if (Peek.Kind == Kind.Open)
            {
                _position++;
                var group = Expression(depth + 1);
                if (_position >= _tokens.Count || Peek.Kind != Kind.Close) throw Unsupported();
                _position++;
                return $"({group})";
            }

            var words = new List<string>();
            while (_position < _tokens.Count && Peek.Kind == Kind.Term)
                words.Add(_tokens[_position++].Text);
            if (words.Count == 0) throw Unsupported();
            var operand = string.Join(' ', words);
            if (_position >= _tokens.Count || Peek.Kind != Kind.Field) return operand;

            var field = _tokens[_position++].Text.ToLowerInvariant();
            var mapped = field switch
            {
                "title/abstract" or "tiab" => "TITLE_ABS",
                "title" or "ti" => "TITLE",
                _ => throw Unsupported()
            };
            return $"{mapped}:({operand})";
        }

        private Token Peek => _tokens[_position];

        private static List<Token> Tokenize(string value)
        {
            var tokens = new List<Token>();
            for (var position = 0; position < value.Length;)
            {
                var character = value[position];
                if (char.IsWhiteSpace(character)) { position++; continue; }
                if (character is '(' or ')')
                {
                    tokens.Add(new Token(character == '(' ? Kind.Open : Kind.Close, character.ToString()));
                    position++;
                    continue;
                }
                if (character == '[')
                {
                    var end = value.IndexOf(']', position + 1);
                    if (end < 0) throw Unsupported();
                    var field = value[(position + 1)..end].Trim();
                    tokens.Add(new Token(Kind.Field, field));
                    position = end + 1;
                    continue;
                }
                if (character == ']') throw Unsupported();
                if (character == '"')
                {
                    var start = position++;
                    while (position < value.Length && value[position] != '"')
                    {
                        if (value[position] == '\\') throw Unsupported();
                        position++;
                    }
                    if (position >= value.Length || string.IsNullOrWhiteSpace(value[(start + 1)..position])) throw Unsupported();
                    tokens.Add(new Token(Kind.Term, value[start..++position]));
                    continue;
                }
                var wordStart = position;
                while (position < value.Length && !char.IsWhiteSpace(value[position]) && value[position] is not ('(' or ')' or '[' or ']' or '"'))
                {
                    // Do not reinterpret mixed native fields, proximity/ranges or special query operators.
                    if (!char.IsLetterOrDigit(value[position]) && value[position] is not ('-' or '*' or '_')) throw Unsupported();
                    position++;
                }
                var word = value[wordStart..position];
                if (!char.IsLetterOrDigit(word[0]) || (word.Contains('*') && word.IndexOf('*') != word.Length - 1)) throw Unsupported();
                tokens.Add(new Token(word is "AND" or "OR" or "NOT" ? Kind.Operator : Kind.Term, word));
            }
            return tokens;
        }
    }

    private enum Kind { Term, Field, Open, Close, Operator }
    private sealed record Token(Kind Kind, string Text);
}

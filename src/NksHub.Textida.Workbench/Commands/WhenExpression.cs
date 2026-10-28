using System.Text;

namespace NksHub.Textida.Workbench.Commands;

/// <summary>Source of context values for <c>when</c> expressions. A missing key returns null.</summary>
public interface IWhenContext
{
    string? Get(string key);
}

/// <summary>Simple dictionary-backed context. Booleans are stored as "true"/"false".</summary>
public sealed class WhenContext : IWhenContext
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public WhenContext Set(string key, bool value) => Set(key, value ? "true" : "false");

    public WhenContext Set(string key, string value)
    {
        _values[key] = value;
        return this;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Names of the context keys the core provides.</summary>
public static class ContextKeys
{
    public const string EditorFocus = "editorFocus";
    public const string HasDocument = "hasDocument";
    public const string Language = "language";
    public const string HasSelection = "hasSelection";
    public const string ReadOnly = "readOnly";
    public const string LargeFileMode = "largeFileMode";
    public const string Platform = "platform";
    public const string IsDirty = "isDirty";
    public const string HasFolder = "hasFolder";
    public const string FileSelected = "fileSelected";
    public const string MultipleCarets = "multipleCarets";
    /// <summary>The active tab shows its document in the hex view.</summary>
    public const string HexMode = "hexMode";
    /// <summary>The active tab shows its document as a table.</summary>
    public const string TableMode = "tableMode";
    /// <summary>The active tab is a comparison of two sides.</summary>
    public const string CompareMode = "compareMode";
    /// <summary>The caret is in a Markdown table with a header and a row of dashes.</summary>
    public const string InMarkdownTable = "inMarkdownTable";
    /// <summary>A snippet session runs in the active editor: Tab and Shift+Tab walk through its stops.</summary>
    public const string InSnippet = "inSnippet";
    /// <summary>A side panel (open documents, files, a plug-in panel) is open: the commands that dock, resize and hide it work on it.</summary>
    public const string PanelOpen = "panelOpen";

    public static IReadOnlyList<string> All { get; } =
        [EditorFocus, HasDocument, Language, HasSelection, ReadOnly, LargeFileMode, Platform, IsDirty, HasFolder, FileSelected, MultipleCarets, HexMode, TableMode, CompareMode, InMarkdownTable, InSnippet, PanelOpen];
}

/// <summary>
/// A parsed <c>when</c> expression. Grammar: <c>||</c>, <c>&amp;&amp;</c>, <c>!</c>, parentheses, <c>true</c>, <c>false</c>,
/// a bare key (true when its value is "true") and <c>key == value</c> / <c>key != value</c> where the value is a quoted
/// string or a bare word. Only data is evaluated; there is no code execution.
/// </summary>
public abstract class WhenExpression
{
    public const int MaxLength = 512;
    public const int MaxDepth = 32;
    private const int MaxCombinations = 1 << 16;
    private const string Other = "<other>";

    private IReadOnlySet<string>? _keys;

    public abstract bool Evaluate(IWhenContext context);

    public abstract override string ToString();

    internal abstract void Collect(Dictionary<string, HashSet<string>> variables);

    /// <summary>Context keys the expression reads.</summary>
    public IReadOnlySet<string> Keys
    {
        get
        {
            if (_keys is null)
            {
                var vars = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                Collect(vars);
                _keys = vars.Keys.ToHashSet(StringComparer.Ordinal);
            }

            return _keys;
        }
    }

    public static bool TryParse(string? text, out WhenExpression? expression, out string? error)
    {
        expression = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Empty expression.";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = $"Expression is longer than {MaxLength} characters.";
            return false;
        }

        try
        {
            expression = new Parser(text).ParseAll();
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static WhenExpression Parse(string text) =>
        TryParse(text, out var expr, out var error) ? expr! : throw new FormatException(error);

    /// <summary>
    /// True when some assignment of context values makes both expressions true (null means "always").
    /// Used to decide whether two contributions can be active at the same time.
    /// Falls back to true (conservative) when the search space is too large.
    /// </summary>
    public static bool CanBothBeTrue(WhenExpression? a, WhenExpression? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        var vars = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        a?.Collect(vars);
        b?.Collect(vars);
        var names = vars.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var domains = names.Select(n => vars[n].Append("true").Append(Other).Distinct().ToArray()).ToArray();

        long total = 1;
        foreach (var d in domains)
        {
            total *= d.Length;
            if (total > MaxCombinations)
            {
                return true;
            }
        }

        var index = new int[names.Length];
        var ctx = new AssignmentContext();
        for (long n = 0; n < total; n++)
        {
            for (var i = 0; i < names.Length; i++)
            {
                ctx.Values[names[i]] = domains[i][index[i]];
            }

            if ((a?.Evaluate(ctx) ?? true) && (b?.Evaluate(ctx) ?? true))
            {
                return true;
            }

            for (var i = 0; i < index.Length; i++)
            {
                if (++index[i] < domains[i].Length)
                {
                    break;
                }

                index[i] = 0;
            }
        }

        return false;
    }

    private sealed class AssignmentContext : IWhenContext
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? Get(string key) => Values.GetValueOrDefault(key);
    }

    private static string Wrap(WhenExpression e) => e is Constant or Key or Compare ? e.ToString() : "(" + e + ")";

    private sealed class Constant(bool value) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => value;

        public override string ToString() => value ? "true" : "false";

        internal override void Collect(Dictionary<string, HashSet<string>> variables)
        {
        }
    }

    private sealed class Key(string name) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => context.Get(name) == "true";

        public override string ToString() => name;

        internal override void Collect(Dictionary<string, HashSet<string>> variables) => Slot(variables, name);
    }

    private sealed class Compare(string name, string value, bool equal) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => (context.Get(name) == value) == equal;

        public override string ToString() => $"{name} {(equal ? "==" : "!=")} '{value.Replace("'", "\\'", StringComparison.Ordinal)}'";

        internal override void Collect(Dictionary<string, HashSet<string>> variables) => Slot(variables, name).Add(value);
    }

    private sealed class Not(WhenExpression inner) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => !inner.Evaluate(context);

        public override string ToString() => "!" + Wrap(inner);

        internal override void Collect(Dictionary<string, HashSet<string>> variables) => inner.Collect(variables);
    }

    private sealed class And(WhenExpression left, WhenExpression right) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => left.Evaluate(context) && right.Evaluate(context);

        public override string ToString() => Wrap(left) + " && " + Wrap(right);

        internal override void Collect(Dictionary<string, HashSet<string>> variables)
        {
            left.Collect(variables);
            right.Collect(variables);
        }
    }

    private sealed class Or(WhenExpression left, WhenExpression right) : WhenExpression
    {
        public override bool Evaluate(IWhenContext context) => left.Evaluate(context) || right.Evaluate(context);

        public override string ToString() => Wrap(left) + " || " + Wrap(right);

        internal override void Collect(Dictionary<string, HashSet<string>> variables)
        {
            left.Collect(variables);
            right.Collect(variables);
        }
    }

    private static HashSet<string> Slot(Dictionary<string, HashSet<string>> variables, string name)
    {
        if (!variables.TryGetValue(name, out var set))
        {
            variables[name] = set = [];
        }

        return set;
    }

    private enum Tok { Word, Str, LParen, RParen, Not, And, Or, Eq, Ne, End }

    private sealed class Parser
    {
        private readonly List<(Tok Kind, string Text)> _tokens = [];
        private int _pos;
        private int _depth;

        public Parser(string text)
        {
            Tokenize(text);
        }

        private void Tokenize(string s)
        {
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '(')
                {
                    _tokens.Add((Tok.LParen, "("));
                    i++;
                }
                else if (c == ')')
                {
                    _tokens.Add((Tok.RParen, ")"));
                    i++;
                }
                else if (c == '!' && i + 1 < s.Length && s[i + 1] == '=')
                {
                    _tokens.Add((Tok.Ne, "!="));
                    i += 2;
                }
                else if (c == '!')
                {
                    _tokens.Add((Tok.Not, "!"));
                    i++;
                }
                else if (c == '=' && i + 1 < s.Length && s[i + 1] == '=')
                {
                    _tokens.Add((Tok.Eq, "=="));
                    i += 2;
                }
                else if (c == '&' && i + 1 < s.Length && s[i + 1] == '&')
                {
                    _tokens.Add((Tok.And, "&&"));
                    i += 2;
                }
                else if (c == '|' && i + 1 < s.Length && s[i + 1] == '|')
                {
                    _tokens.Add((Tok.Or, "||"));
                    i += 2;
                }
                else if (c is '\'' or '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    var closed = false;
                    while (i < s.Length)
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            sb.Append(s[i + 1]);
                            i += 2;
                        }
                        else if (s[i] == c)
                        {
                            closed = true;
                            i++;
                            break;
                        }
                        else
                        {
                            sb.Append(s[i++]);
                        }
                    }

                    if (!closed)
                    {
                        throw new FormatException("Unterminated string.");
                    }

                    _tokens.Add((Tok.Str, sb.ToString()));
                }
                else if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')
                {
                    var start = i;
                    while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] is '_' or '.' or '-'))
                    {
                        i++;
                    }

                    _tokens.Add((Tok.Word, s[start..i]));
                }
                else
                {
                    throw new FormatException($"Unexpected character '{c}' at {i}.");
                }
            }

            _tokens.Add((Tok.End, ""));
        }

        public WhenExpression ParseAll()
        {
            var result = ParseOr();
            if (Peek.Kind != Tok.End)
            {
                throw new FormatException($"Unexpected '{Peek.Text}'.");
            }

            return result;
        }

        private (Tok Kind, string Text) Peek => _tokens[_pos];

        private void Enter()
        {
            if (++_depth > MaxDepth)
            {
                throw new FormatException($"Expression is nested deeper than {MaxDepth}.");
            }
        }

        private WhenExpression ParseOr()
        {
            Enter();
            var left = ParseAnd();
            while (Peek.Kind == Tok.Or)
            {
                _pos++;
                left = new Or(left, ParseAnd());
            }

            _depth--;
            return left;
        }

        private WhenExpression ParseAnd()
        {
            var left = ParseUnary();
            while (Peek.Kind == Tok.And)
            {
                _pos++;
                left = new And(left, ParseUnary());
            }

            return left;
        }

        private WhenExpression ParseUnary()
        {
            if (Peek.Kind != Tok.Not)
            {
                return ParsePrimary();
            }

            _pos++;
            Enter();
            var inner = ParseUnary();
            _depth--;
            return new Not(inner);
        }

        private WhenExpression ParsePrimary()
        {
            var (kind, text) = Peek;
            switch (kind)
            {
                case Tok.LParen:
                    _pos++;
                    var inner = ParseOr();
                    if (Peek.Kind != Tok.RParen)
                    {
                        throw new FormatException("Missing ')'.");
                    }

                    _pos++;
                    return inner;
                case Tok.Word:
                    _pos++;
                    if (Peek.Kind is Tok.Eq or Tok.Ne)
                    {
                        var equal = Peek.Kind == Tok.Eq;
                        _pos++;
                        var (vk, vt) = Peek;
                        if (vk is not (Tok.Word or Tok.Str))
                        {
                            throw new FormatException($"Expected a value after '{(equal ? "==" : "!=")}'.");
                        }

                        _pos++;
                        RequireKeyName(text);
                        return new Compare(text, vt, equal);
                    }

                    if (text == "true")
                    {
                        return new Constant(true);
                    }

                    if (text == "false")
                    {
                        return new Constant(false);
                    }

                    RequireKeyName(text);
                    return new Key(text);
                default:
                    throw new FormatException(kind == Tok.End ? "Unexpected end of expression." : $"Unexpected '{text}'.");
            }
        }

        private static void RequireKeyName(string name)
        {
            if (!(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            {
                throw new FormatException($"'{name}' is not a valid context key.");
            }
        }
    }
}

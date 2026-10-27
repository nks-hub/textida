using System.Text;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Plugins.Formatters;

/// <summary>Letter case of the words the grammar of the dialect lists (keywords, types, functions, constants).</summary>
public enum KeywordCase { Upper, Lower, Preserve }

/// <param name="Indent">Spaces per level.</param>
public sealed record SqlFormatOptions(KeywordCase Case, int Indent);

/// <summary>
/// Layout of SQL text without a database: statements, clauses on their own lines, one select item per line, sub-queries indented, keywords in the chosen case.
/// Strings, quoted names and comments are copied as written. The words that count as keywords come from the grammar of the dialect, so the colors and the
/// formatting agree. Formatting the result again changes nothing.
/// </summary>
public static class SqlFormatter
{
    private enum Kind { Word, Quoted, LineComment, BlockComment, Number, Symbol }

    private sealed record Tok(Kind Kind, string Text, int NewlinesBefore, bool SpaceBefore, string? Word);

    private enum FrameKind { Top, Sub, Inline, Function }

    private sealed class Frame(FrameKind kind, int baseLevel)
    {
        public FrameKind Kind = kind;
        public int Base = baseLevel;
        public string Clause = "";
        public bool Between;
        public int Cases;
        public bool Breaks => Kind is FrameKind.Top or FrameKind.Sub;
    }

    public static string Format(string text, string dialect, string newLine, SqlFormatOptions options)
    {
        var grammar = BuiltinGrammars.Registry.Find(SqlDialects.GrammarId(dialect)) ?? BuiltinGrammars.Registry.Find("sql")!;
        var tokens = Lex(text, dialect, grammar, out int lead, out int trail);
        var body = new Layout(tokens, dialect, newLine, options).Run();
        return string.Concat(text.AsSpan(0, lead), body, text.AsSpan(text.Length - trail));
    }

    // ---- lexing ----

    private static List<Tok> Lex(string text, string dialect, Grammar grammar, out int lead, out int trail)
    {
        var list = new List<Tok>();
        int n = text.Length, i = 0;
        while (i < n && char.IsWhiteSpace(text[i])) i++;
        lead = i;
        int end = n;
        while (end > lead && char.IsWhiteSpace(text[end - 1])) end--;
        trail = n - end;
        bool mysql = dialect == "mysql", tsql = dialect == "tsql", dollars = dialect is "postgres" or SqlDialects.Generic;
        int newlines = 0;
        bool space = false;
        void Add(Kind kind, int from, int to, string? word = null)
        {
            list.Add(new Tok(kind, text[from..to], newlines, space, word));
            newlines = 0;
            space = false;
        }
        while (i < end)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (c == '\n') newlines++;
                space = true;
                i++;
                continue;
            }
            int start = i;
            if (c == '-' && i + 1 < end && text[i + 1] == '-' || mysql && c == '#')
            {
                while (i < end && text[i] is not ('\n' or '\r')) i++;
                Add(Kind.LineComment, start, i);
            }
            else if (c == '/' && i + 1 < end && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 || close + 2 > end ? end : close + 2;
                Add(Kind.BlockComment, start, i);
            }
            else if (c is '\'' or '"' or '`' || tsql && c == '[')
            {
                char quote = c == '[' ? ']' : c;
                i++;
                while (i < end)
                {
                    if (mysql && text[i] == '\\' && i + 1 < end && c == '\'') { i += 2; continue; }
                    if (text[i] == quote)
                    {
                        if (quote != ']' && i + 1 < end && text[i + 1] == quote) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                Add(Kind.Quoted, start, i);
            }
            else if (dollars && c == '$' && DollarTagLength(text, i, end) is var tag and > 0)
            {
                int close = text.IndexOf(text.Substring(i, tag), i + tag, end - i - tag, StringComparison.Ordinal);
                i = close < 0 ? end : close + tag;
                Add(Kind.Quoted, start, i);
            }
            else if (char.IsAsciiDigit(c) || c == '.' && i + 1 < end && char.IsAsciiDigit(text[i + 1]))
            {
                if (c == '0' && i + 1 < end && text[i + 1] is 'x' or 'X') { i += 2; while (i < end && char.IsAsciiHexDigit(text[i])) i++; }
                else
                {
                    while (i < end && (char.IsAsciiDigit(text[i]) || text[i] == '.' && i + 1 < end && char.IsAsciiDigit(text[i + 1]))) i++;
                    if (i < end && text[i] is 'e' or 'E' && i + 1 < end && (char.IsAsciiDigit(text[i + 1]) || text[i + 1] is '+' or '-' && i + 2 < end && char.IsAsciiDigit(text[i + 2])))
                    {
                        i += 2;
                        while (i < end && char.IsAsciiDigit(text[i])) i++;
                    }
                }
                Add(Kind.Number, start, i);
            }
            else if (char.IsLetter(c) || c == '_' || (c is '@' or '$' or '#') && i + 1 < end && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] is '_' or '@')
                     || c == ':' && i + 1 < end && char.IsLetter(text[i + 1]) && (i == 0 || text[i - 1] != ':'))
            {
                i++;
                while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$' or '@' or '#')) i++;
                string word = text[start..i];
                // names that start with a sign are parameters and variables, never keywords
                Add(Kind.Word, start, i, char.IsLetter(c) || c == '_' ? grammar.WordKind(word) : null);
            }
            else
            {
                i += SymbolLength(text, i, end);
                Add(Kind.Symbol, start, i);
            }
        }
        return list;
    }

    private static int DollarTagLength(string text, int at, int end)
    {
        int j = at + 1;
        while (j < end && (char.IsLetter(text[j]) || text[j] == '_')) j++;
        return j < end && text[j] == '$' ? j - at + 1 : 0;
    }

    private static readonly string[] LongSymbols = ["->>", "<>", "<=", ">=", "!=", "||", "::", "->", "<<", ">>", ":=", "=>"];

    private static int SymbolLength(string text, int at, int end)
    {
        foreach (var s in LongSymbols)
            if (string.CompareOrdinal(text, at, s, 0, s.Length) == 0 && at + s.Length <= end) return s.Length;
        return 1;
    }

    // ---- layout ----

    private sealed class Layout(List<Tok> tokens, string dialect, string newLine, SqlFormatOptions options)
    {
        private readonly StringBuilder _out = new();
        private readonly Stack<Frame> _frames = new([new Frame(FrameKind.Top, 0)]);
        private int _lineStart;
        private int _lineLevel;
        private bool _atLineStart = true;
        private Tok? _prev;

        private Frame Frame => _frames.Peek();

        private static readonly string[] JoinModifiers = ["natural", "inner", "left", "right", "full", "cross", "outer"];

        public string Run()
        {
            for (int i = 0; i < tokens.Count; i++) i += Step(i);
            return _out.ToString().TrimEnd();
        }

        private string Lower(int i) => tokens[i].Kind == Kind.Word ? tokens[i].Text.ToLowerInvariant() : "";

        private bool IsWord(int i, string word) => i < tokens.Count && tokens[i].Kind == Kind.Word && Lower(i) == word;

        private bool IsSymbol(int i, string symbol) => i < tokens.Count && tokens[i].Kind == Kind.Symbol && tokens[i].Text == symbol;

        /// <summary>The clause keywords that start at token <paramref name="i"/>: how many tokens they take and the clause name; 0 when none does.</summary>
        private int ClauseAt(int i, out string name)
        {
            name = "";
            if (tokens[i].Kind != Kind.Word || !Frame.Breaks) return 0;
            string w = Lower(i);
            switch (w)
            {
                case "select": case "from": case "where": case "having": case "limit": case "offset": case "intersect": case "except": case "values": case "returning":
                    name = w;
                    return 1;
                case "union":
                    name = "union";
                    return IsWord(i + 1, "all") ? 2 : 1;
                case "group" or "order" when IsWord(i + 1, "by"):
                    name = w + " by";
                    return 2;
                case "insert" when IsWord(i + 1, "into"):
                    name = "insert";
                    return 2;
                case "delete" when IsWord(i + 1, "from"):
                    name = "delete";
                    return 2;
                case "update" when Frame.Clause == "":
                    name = "update";
                    return 1;
                case "set" when Frame.Clause == "update":
                    name = "set";
                    return 1;
            }
            int j = i;
            while (j < tokens.Count && tokens[j].Kind == Kind.Word && JoinModifiers.Contains(Lower(j))) j++;
            if (j < tokens.Count && tokens[j].Kind == Kind.Word && (Lower(j) == "join" || dialect == "tsql" && Lower(j) == "apply" && j > i))
            {
                name = "join";
                return j - i + 1;
            }
            return 0;
        }

        private static bool ListClause(string clause) => clause is "select" or "group by" or "order by" or "set" or "values";

        private static bool ConditionClause(string clause) => clause is "where" or "having" or "on";

        private bool StartsClauseOrEnds(int i) => i < tokens.Count && (ClauseAt(i, out _) > 0 || IsSymbol(i, ")"));

        // ---- output ----

        private void StartLine(int level)
        {
            if (_atLineStart) _out.Length = _lineStart;
            else
            {
                while (_out.Length > 0 && _out[^1] == ' ') _out.Length--;
                if (_out.Length > 0) _out.Append(newLine);
            }
            _lineStart = _out.Length;
            _out.Append(' ', level * options.Indent);
            _lineLevel = level;
            _atLineStart = true;
        }

        private void Put(string text, bool space)
        {
            bool tight = _tightAfter;
            _tightAfter = false;
            if (!_atLineStart && space && !tight) _out.Append(' ');
            _out.Append(text);
            _atLineStart = false;
        }

        private string CaseOf(Tok t)
        {
            if (t.Word is null or "" || options.Case == KeywordCase.Preserve) return t.Text;
            if (_prev is { Kind: Kind.Symbol, Text: "." }) return t.Text;
            return options.Case == KeywordCase.Upper ? t.Text.ToUpperInvariant() : t.Text.ToLowerInvariant();
        }

        private static bool IsOperand(Tok? t) =>
            t is { Kind: Kind.Number or Kind.Quoted } || t is { Kind: Kind.Word, Word: not "keyword" } || t is { Kind: Kind.Symbol, Text: ")" or "]" };

        private static readonly HashSet<string> Tight = ["::", ".", "[", "]"];

        private bool Spaced(Tok t)
        {
            var p = _prev;
            if (p is null) return false;
            if (t.Kind == Kind.Symbol)
            {
                if (t.Text is "," or ";" or ")" || Tight.Contains(t.Text)) return false;
                if (t.Text == "(")
                {
                    if (p.Kind == Kind.Word) return p.Word is "keyword" or "constant" || p.Word is null && t.SpaceBefore;
                    if (p.Kind == Kind.Symbol) return p.Text is not ("(" or "." or "::" or "[") && (p.Text != ")" || t.SpaceBefore);
                    return t.SpaceBefore;
                }
            }
            if (p.Kind == Kind.Symbol && (p.Text is "(" or "." or "::" or "[" )) return false;
            if (t.Kind == Kind.Quoted && p.Kind == Kind.Word && !t.SpaceBefore && t.Text[0] == '\'') return false;
            return true;
        }

        private bool _tightAfter;

        // ---- one token ----

        /// <summary>Lays out token <paramref name="i"/> (and the clause keywords that go with it); returns how many extra tokens it took.</summary>
        private int Step(int i)
        {
            var t = tokens[i];
            var frame = Frame;
            switch (t.Kind)
            {
                case Kind.LineComment:
                case Kind.BlockComment:
                    return Comment(i);
                case Kind.Symbol:
                    Symbol(i, t);
                    return 0;
            }

            if (t.Kind == Kind.Word)
            {
                string w = Lower(i);
                int taken = ClauseAt(i, out var clause);
                if (taken > 0)
                {
                    frame.Clause = clause;
                    StartLine(frame.Base);
                    var words = new List<string>();
                    for (int k = 0; k < taken; k++) words.Add(CaseOf(tokens[i + k]));
                    Put(string.Join(' ', words), false);
                    _prev = tokens[i + taken - 1];
                    if (ListClause(clause)) StartLine(frame.Base + 1);
                    return taken - 1;
                }
                if (frame.Breaks)
                {
                    if (w == "on" && frame.Clause == "join") frame.Clause = "on";
                    else if (w == "go" && dialect == "tsql" && (t.NewlinesBefore > 0 || _prev is null))
                    {
                        StartLine(0);
                        Put(CaseOf(t), false);
                        frame.Clause = "";
                        _prev = t;
                        StartLine(0);
                        return 0;
                    }
                    else if (w is "and" or "or" && ConditionClause(frame.Clause) && frame.Cases == 0)
                    {
                        if (w == "and" && frame.Between) frame.Between = false;
                        else
                        {
                            StartLine(frame.Base + 1);
                            Put(CaseOf(t), false);
                            _prev = t;
                            return 0;
                        }
                    }
                }
                if (w == "between") frame.Between = true;
                else if (w == "case") frame.Cases++;
                else if (w == "end" && frame.Cases > 0) frame.Cases--;
                else if (w == "and" && frame.Between) frame.Between = false;
            }

            Put(t.Kind == Kind.Word ? CaseOf(t) : t.Text, Spaced(t));
            _prev = t;
            return 0;
        }

        private int Comment(int i)
        {
            var t = tokens[i];
            var frame = Frame;
            bool own = t.NewlinesBefore > 0 || _prev is null;
            bool afterSemicolon = !own && _prev is { Kind: Kind.Symbol, Text: ";" };
            if (own)
            {
                int next = i + 1;
                while (next < tokens.Count && tokens[next].Kind is Kind.LineComment or Kind.BlockComment) next++;
                int level = StartsClauseOrEnds(next) || next >= tokens.Count ? frame.Base : ListClause(frame.Clause) || ConditionClause(frame.Clause) ? frame.Base + 1 : frame.Base;
                StartLine(level);
                Put(t.Text, false);
            }
            else Put(t.Text, true);
            _prev = t;
            if (t.Kind == Kind.LineComment || own && i + 1 < tokens.Count && tokens[i + 1].NewlinesBefore > 0) StartLine(_lineLevel);
            if (afterSemicolon && i + 1 < tokens.Count) NextStatement(i + 1);
            return 0;
        }

        private void NextStatement(int next)
        {
            StartLine(0);
            if (tokens[next].NewlinesBefore > 1) { _out.Append(newLine); _lineStart = _out.Length; }
        }

        private void Symbol(int i, Tok t)
        {
            var frame = Frame;
            switch (t.Text)
            {
                case ";":
                    Put(";", false);
                    _prev = t;
                    while (_frames.Count > 1) _frames.Pop();
                    _frames.Peek().Clause = "";
                    _frames.Peek().Between = false;
                    _frames.Peek().Cases = 0;
                    // a comment on the same line as the semicolon stays there
                    if (i + 1 < tokens.Count && !(tokens[i + 1].Kind is Kind.LineComment or Kind.BlockComment && tokens[i + 1].NewlinesBefore == 0)) NextStatement(i + 1);
                    return;
                case "(":
                    {
                        bool sub = i + 1 < tokens.Count && (IsWord(i + 1, "select") || IsWord(i + 1, "with"));
                        Put("(", Spaced(t));
                        var kind = sub ? FrameKind.Sub : _prev is { Kind: Kind.Word, Word: not ("keyword" or "constant") } ? FrameKind.Function : FrameKind.Inline;
                        _prev = t;
                        _frames.Push(new Frame(kind, _lineLevel + 1));
                        if (sub) StartLine(_frames.Peek().Base);
                        return;
                    }
                case ")":
                    {
                        if (_frames.Count > 1)
                        {
                            var closing = _frames.Pop();
                            if (closing.Kind == FrameKind.Sub) StartLine(closing.Base - 1);
                        }
                        Put(")", false);
                        _prev = t;
                        return;
                    }
                case ",":
                    Put(",", false);
                    _prev = t;
                    if (frame.Breaks && ListClause(frame.Clause)) StartLine(frame.Base + 1);
                    return;
            }
            bool operandBefore = IsOperand(_prev);
            bool unary = t.Text is "-" or "+" && !operandBefore;
            bool star = t.Text == "*" && !operandBefore;
            Put(t.Text, Spaced(t));
            _tightAfter = unary || star;
            _prev = t;
        }
    }
}

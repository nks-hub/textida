using System.Text;

namespace NksHub.Textida.Core.Snippets;

/// <summary>A stretch of the snippet text: UTF-16 offset and length.</summary>
public readonly record struct SnippetSpan(int Start, int Length);

/// <summary>
/// One tab stop. <paramref name="Spans"/> are all its places in the text; the first is the one the user edits, the others mirror it.
/// <paramref name="Choices"/> are the values offered by <c>${1|a,b|}</c> (empty for a plain stop).
/// </summary>
public sealed record SnippetStop(int Number, IReadOnlyList<SnippetSpan> Spans, IReadOnlyList<string> Choices);

/// <summary>
/// A parsed snippet. <paramref name="Stops"/> are in the order Tab visits them (1, 2, ...); <paramref name="FinalOffset"/> is <c>$0</c>, or the end of the text.
/// <paramref name="Missing"/> are variables the resolver did not know (their name stands in the text).
/// </summary>
public sealed record ParsedSnippet(string Text, IReadOnlyList<SnippetStop> Stops, int FinalOffset, IReadOnlyList<string> Missing)
{
    /// <summary>
    /// Puts <paramref name="baseIndent"/> in front of every line after the first (empty lines stay empty unless a stop is on them) and turns the tabs in
    /// the leading whitespace of those lines into <paramref name="indentUnit"/>. Stops move with the text.
    /// </summary>
    public ParsedSnippet Reindent(string baseIndent, string indentUnit, string newLine = "\n")
    {
        if (Text.IndexOf(newLine, StringComparison.Ordinal) < 0) return this;
        var marks = new HashSet<int>(Stops.SelectMany(s => s.Spans.Select(x => x.Start)).Append(FinalOffset));
        var map = new int[Text.Length + 1];
        var sb = new StringBuilder(Text.Length + 16);
        int pos = 0;
        while (true)
        {
            int lineEnd = Text.IndexOf(newLine, pos, StringComparison.Ordinal);
            bool last = lineEnd < 0;
            if (last) lineEnd = Text.Length;
            int k = pos;
            if (pos > 0)
            {
                if (lineEnd > pos || marks.Contains(pos)) sb.Append(baseIndent);
                map[pos] = sb.Length;
                for (; k < lineEnd && Text[k] is ' ' or '\t'; k++)
                {
                    map[k] = sb.Length;
                    sb.Append(Text[k] == '\t' ? indentUnit : " ");
                }
            }
            for (; k < lineEnd; k++)
            {
                map[k] = sb.Length;
                sb.Append(Text[k]);
            }
            map[lineEnd] = sb.Length;
            if (last) break;
            for (int n = 0; n < newLine.Length; n++)
            {
                sb.Append(newLine[n]);
                map[lineEnd + n + 1] = sb.Length;
            }
            pos = lineEnd + newLine.Length;
        }
        var stops = Stops.Select(s => s with { Spans = [.. s.Spans.Select(x => new SnippetSpan(map[x.Start], map[x.Start + x.Length] - map[x.Start]))] }).ToList();
        return new ParsedSnippet(sb.ToString(), stops, map[FinalOffset], Missing);
    }
}

/// <summary>
/// The snippet syntax of the language servers (a subset of TextMate): <c>$1</c>, <c>${1}</c>, <c>${1:default}</c>, <c>${1|a,b,c|}</c>, <c>$0</c>, variables
/// <c>$NAME</c> / <c>${NAME}</c> / <c>${NAME:default}</c>, and the escapes <c>\$</c>, <c>\}</c>, <c>\\</c> (also <c>$$</c> for a dollar sign). The same number in
/// several places is one stop edited together. Nested stops and transforms are not supported; anything that does not parse stays in the text as it is.
/// </summary>
public static class SnippetParser
{
    /// <summary>True when <paramref name="template"/> has a numbered stop, so it is a snippet and not just text with variables.</summary>
    public static bool HasStops(string template)
    {
        for (int i = 0; i + 1 < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\') i++;
            else if (c == '$')
            {
                char n = template[i + 1];
                if (n == '$') i++;
                else if (char.IsAsciiDigit(n) || (n == '{' && i + 2 < template.Length && char.IsAsciiDigit(template[i + 2]))) return true;
            }
        }
        return false;
    }

    /// <summary>Parses <paramref name="template"/>; <paramref name="variable"/> gives the value of a variable or null when it has none. Line breaks become <paramref name="newLine"/>.</summary>
    public static ParsedSnippet Parse(string template, Func<string, string?>? variable = null, string newLine = "\n") =>
        new Reader(template, variable, newLine).Run();

    private sealed class Piece
    {
        public string? Literal;
        public int Number = -1;
        public string? Default;
        public List<string>? Choices;
    }

    private sealed class Reader(string t, Func<string, string?>? variable, string newLine)
    {
        private readonly List<Piece> _pieces = [];
        private readonly List<string> _missing = [];
        private int _i;

        public ParsedSnippet Run()
        {
            var lit = new StringBuilder();
            Text(lit, inDefault: false);
            Flush(lit);

            var defaults = new Dictionary<int, Piece>();
            foreach (var p in _pieces)
                if (p.Number >= 0 && (p.Default is not null || p.Choices is not null)) defaults.TryAdd(p.Number, p);
            var sb = new StringBuilder();
            var spans = new SortedDictionary<int, List<SnippetSpan>>();
            int final = -1;
            foreach (var p in _pieces)
            {
                if (p.Literal is not null) { sb.Append(p.Literal); continue; }
                string value = defaults.TryGetValue(p.Number, out var d) ? d.Choices is { Count: > 0 } c ? c[0] : d.Default ?? "" : "";
                if (p.Number == 0) { if (final < 0) final = sb.Length; }
                else
                {
                    if (!spans.TryGetValue(p.Number, out var list)) spans[p.Number] = list = [];
                    list.Add(new SnippetSpan(sb.Length, value.Length));
                }
                sb.Append(value);
            }
            var stops = spans.Select(s => new SnippetStop(s.Key, s.Value, defaults.TryGetValue(s.Key, out var d) && d.Choices is { Count: > 1 } c ? c : [])).ToList();
            return new ParsedSnippet(sb.ToString(), stops, final < 0 ? sb.Length : final, _missing);
        }

        private void Flush(StringBuilder lit)
        {
            if (lit.Length == 0) return;
            _pieces.Add(new Piece { Literal = lit.ToString() });
            lit.Clear();
        }

        private void Append(StringBuilder sb, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                if (s[i] is '\r' or '\n') sb.Append(newLine); else sb.Append(s[i]);
            }
        }

        /// <summary>Reads text up to the end, or in a default up to the closing brace (not consumed).</summary>
        private void Text(StringBuilder lit, bool inDefault)
        {
            while (_i < t.Length)
            {
                char c = t[_i];
                if (inDefault && c == '}') return;
                if (c == '\\' && _i + 1 < t.Length && t[_i + 1] is '$' or '}' or '\\') { lit.Append(t[_i + 1]); _i += 2; }
                else if (c == '$' && Dollar(lit, inDefault)) { }
                else if (c is '\r' or '\n')
                {
                    if (c == '\r' && _i + 1 < t.Length && t[_i + 1] == '\n') _i++;
                    lit.Append(newLine);
                    _i++;
                }
                else { lit.Append(c); _i++; }
            }
        }

        private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';

        private static bool IsNameChar(char c, bool dots) => char.IsAsciiLetterOrDigit(c) || c == '_' || (dots && c == '.');

        private int ReadWhile(int from, Func<char, bool> accept)
        {
            int j = from;
            while (j < t.Length && accept(t[j])) j++;
            return j;
        }

        /// <summary>Tries the construct at the dollar sign; false leaves <c>_i</c> untouched so the dollar stays a plain character.</summary>
        private bool Dollar(StringBuilder lit, bool inDefault)
        {
            int start = _i;
            if (start + 1 >= t.Length) return false;
            char n = t[start + 1];
            if (n == '$') { lit.Append('$'); _i += 2; return true; }
            int missingBefore = _missing.Count;
            bool braced = n == '{';
            int at = start + (braced ? 2 : 1);
            if (at >= t.Length) return false;

            if (char.IsAsciiDigit(t[at]))
            {
                int end = ReadWhile(at, char.IsAsciiDigit);
                if (end - at > 6 || !int.TryParse(t.AsSpan(at, end - at), out int number)) return false;
                if (!braced) return Stop(lit, inDefault, number, end, null, null);
                if (end >= t.Length) return false;
                if (t[end] == '}') return Stop(lit, inDefault, number, end + 1, null, null);
                if (t[end] == ':')
                {
                    _i = end + 1;
                    var def = new StringBuilder();
                    Text(def, inDefault: true);
                    if (_i < t.Length && t[_i] == '}') return Stop(lit, inDefault, number, _i + 1, def.ToString(), null);
                    return Fail(start, missingBefore);
                }
                if (t[end] == '|' && Choices(end + 1) is { } choices && choices.next < t.Length && t[choices.next] == '}')
                    return Stop(lit, inDefault, number, choices.next + 1, null, choices.items);
                return Fail(start, missingBefore);
            }

            if (!IsNameStart(t[at])) return false;
            int nameEnd = ReadWhile(at, ch => IsNameChar(ch, braced));
            string name = t.Substring(at, nameEnd - at);
            if (!braced)
            {
                // only the uppercase names of the standard variables; $this or $value in code stay as they are
                if (!char.IsAsciiLetterUpper(name[0]) || variable?.Invoke(name) is not { } known) return false;
                Append(lit, known);
                _i = nameEnd;
                return true;
            }
            if (nameEnd >= t.Length) return false;
            if (t[nameEnd] == '}') { Variable(lit, name, null); _i = nameEnd + 1; return true; }
            if (t[nameEnd] == ':')
            {
                _i = nameEnd + 1;
                var def = new StringBuilder();
                Text(def, inDefault: true);
                if (_i < t.Length && t[_i] == '}')
                {
                    Variable(lit, name, def.ToString());
                    _i++;
                    return true;
                }
            }
            return Fail(start, missingBefore);
        }

        private bool Fail(int start, int missingBefore)
        {
            _i = start;
            _missing.RemoveRange(missingBefore, _missing.Count - missingBefore);
            return false;
        }

        private bool Stop(StringBuilder lit, bool inDefault, int number, int next, string? def, List<string>? choices)
        {
            if (inDefault) return false; // nested stops are not supported
            Flush(lit);
            _pieces.Add(new Piece { Number = number, Default = def, Choices = choices });
            _i = next;
            return true;
        }

        private void Variable(StringBuilder lit, string name, string? fallback)
        {
            if (variable?.Invoke(name) is { } value) Append(lit, value);
            else if (fallback is not null) lit.Append(fallback);
            else
            {
                lit.Append(name);
                if (!_missing.Contains(name)) _missing.Add(name);
            }
        }

        /// <summary>Reads the items of <c>|a,b,c|</c> from <paramref name="from"/> (after the first bar); null when it is not closed.</summary>
        private (List<string> items, int next)? Choices(int from)
        {
            var items = new List<string>();
            var cur = new StringBuilder();
            for (int j = from; j < t.Length; j++)
            {
                char c = t[j];
                if (c == '\\' && j + 1 < t.Length && t[j + 1] is ',' or '|' or '\\') { cur.Append(t[++j]); continue; }
                if (c == ',') { Add(items, cur); continue; }
                if (c == '|')
                {
                    Add(items, cur);
                    return (items, j + 1);
                }
                cur.Append(c);
            }
            return null;

            void Add(List<string> list, StringBuilder sb)
            {
                var one = new StringBuilder();
                Append(one, sb.ToString());
                list.Add(one.ToString());
                sb.Clear();
            }
        }
    }
}

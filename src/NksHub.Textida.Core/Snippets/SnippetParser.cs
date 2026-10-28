using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Snippets;

/// <summary>A stretch of the snippet text: UTF-16 offset and length. A <paramref name="Transform"/> marks a mirror that shows the transformed text of its stop.</summary>
public readonly record struct SnippetSpan(int Start, int Length, SnippetTransform? Transform = null);

/// <summary>
/// One tab stop. <paramref name="Spans"/> are all its places in the text; the first is the one the user edits, the others mirror it (some through a transform).
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
        var stops = Stops.Select(s => s with { Spans = [.. s.Spans.Select(x => x with { Start = map[x.Start], Length = map[x.Start + x.Length] - map[x.Start] })] }).ToList();
        return new ParsedSnippet(sb.ToString(), stops, map[FinalOffset], Missing);
    }
}

/// <summary>
/// The snippet syntax of the language servers (TextMate): <c>$1</c>, <c>${1}</c>, <c>${1:default}</c>, <c>${1|a,b,c|}</c>, <c>$0</c>, variables
/// <c>$NAME</c> / <c>${NAME}</c> / <c>${NAME:default}</c>, and the escapes <c>\$</c>, <c>\}</c>, <c>\\</c> (also <c>$$</c> for a dollar sign). The same number in
/// several places is one stop edited together. A default may hold more stops and variables (<c>${1:for ${2:i}}</c>); a stop whose default holds stops is the place
/// that is edited, and its other places show the plain text. A transform, <c>${1/regex/format/options}</c> or <c>${NAME/regex/format/options}</c>, is a mirror
/// that shows the text of its stop or variable changed (see <see cref="SnippetTransform"/>). Anything that does not parse stays in the text as it is.
/// </summary>
public static class SnippetParser
{
    /// <summary>True when <paramref name="template"/> has a numbered stop (or a variable with a transform), so it is a snippet and not just text with variables.</summary>
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
                else if (char.IsAsciiDigit(n)) return true;
                else if (n == '{' && i + 2 < template.Length)
                {
                    if (char.IsAsciiDigit(template[i + 2])) return true;
                    int j = i + 2;
                    while (j < template.Length && (char.IsAsciiLetterOrDigit(template[j]) || template[j] == '_')) j++;
                    if (j > i + 2 && j < template.Length && template[j] == '/') return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Parses <paramref name="template"/>; <paramref name="variable"/> gives the value of a variable or null when it has none. Line breaks become <paramref name="newLine"/>.
    /// Throws <see cref="SnippetLimitException"/> when the text grows past <see cref="SnippetLimits.MaxChars"/>, a transform runs too long or the template is too tangled to read.
    /// </summary>
    public static ParsedSnippet Parse(string template, Func<string, string?>? variable = null, string newLine = "\n", SnippetLimits? limits = null) =>
        new Reader(template, variable, newLine, limits ?? SnippetLimits.Fallback).Run();

    private sealed class Node
    {
        public string? Literal;
        public int Number = -1;
        public List<Node>? Content;
        public List<string>? Choices;
        public SnippetTransform? Transform;
    }

    private sealed class Reader(string t, Func<string, string?>? variable, string newLine, SnippetLimits limits)
    {
        private readonly List<string> _missing = [];
        private readonly long _stepBudget = Math.Max(1024, 32L * t.Length);
        private long _steps;
        private int _i;
        private int _depth;

        // the first definition of each number (a stop with a default or choices), and what the numbers are used for
        private readonly Dictionary<int, Node> _defs = [];
        private readonly HashSet<int> _plain = [];
        private readonly Dictionary<int, Node> _firstTransform = [];

        private readonly Dictionary<int, string> _flat = [];
        private readonly HashSet<int> _active = [];
        private readonly SortedDictionary<int, List<SnippetSpan>> _spans = [];
        private readonly Dictionary<int, int> _primary = [];
        private readonly HashSet<int> _locked = [];
        private int _final = -1;

        public ParsedSnippet Run()
        {
            var tree = Nodes(inDefault: false);
            Collect(tree);
            var sb = new StringBuilder();
            Render(tree, sb, main: true);
            var stops = new List<SnippetStop>(_spans.Count);
            foreach (var (number, list) in _spans)
            {
                if (_primary.TryGetValue(number, out int p) && p > 0)
                {
                    var edited = list[p];
                    list.RemoveAt(p);
                    list.Insert(0, edited);
                }
                stops.Add(new SnippetStop(number, list, _defs.TryGetValue(number, out var d) && d.Choices is { Count: > 1 } c ? c : []));
            }
            return new ParsedSnippet(sb.ToString(), stops, _final < 0 ? sb.Length : _final, _missing);
        }

        // ---- expansion of the tree ----

        private void Collect(List<Node> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Number < 0) continue;
                if (n.Content is not null || n.Choices is not null) _defs.TryAdd(n.Number, n);
                if (n.Transform is null) _plain.Add(n.Number);
                else _firstTransform.TryAdd(n.Number, n);
                if (n.Content is not null && ReferenceEquals(_defs[n.Number], n)) Collect(n.Content);
            }
        }

        private static bool HasNumbered(List<Node> nodes) => nodes.Any(n => n.Number > 0 || (n.Content is not null && HasNumbered(n.Content)));

        private void Check(StringBuilder sb)
        {
            if (sb.Length > limits.MaxChars) throw new SnippetLimitException($"The snippet is longer than {limits.MaxChars} characters.");
        }

        private string Transformed(SnippetTransform transform, string text)
        {
            try { return transform.Apply(text); }
            catch (RegexMatchTimeoutException) { throw new SnippetLimitException("A transform of the snippet took too long."); }
        }

        /// <summary>The text a stop shows where it is not the place that is edited: its choice or default with the stops in it replaced by theirs.</summary>
        private string Flat(int number)
        {
            if (_flat.TryGetValue(number, out var known)) return known;
            string value = "";
            if (_defs.TryGetValue(number, out var def))
            {
                if (def.Choices is { Count: > 0 } choices) value = choices[0];
                else
                {
                    var sb = new StringBuilder();
                    _active.Add(number);
                    Render(def.Content!, sb, main: false);
                    _active.Remove(number);
                    value = sb.ToString();
                }
            }
            return _flat[number] = value;
        }

        /// <summary>Appends the text of <paramref name="nodes"/>; with <paramref name="main"/> the places of the stops and <c>$0</c> are recorded too.</summary>
        private void Render(List<Node> nodes, StringBuilder sb, bool main)
        {
            foreach (var n in nodes)
            {
                if (n.Literal is not null) { sb.Append(n.Literal); Check(sb); continue; }
                int number = n.Number;
                if (_active.Contains(number)) continue; // a stop inside its own default
                int start = sb.Length;
                bool isDef = _defs.TryGetValue(number, out var def) && ReferenceEquals(def, n);
                // a stop that has only transformed places is edited in the first of them
                var transform = n.Transform is not null && (_plain.Contains(number) || !ReferenceEquals(_firstTransform[number], n)) ? n.Transform : null;
                if (transform is not null) sb.Append(Transformed(transform, Flat(number)));
                else if (isDef && n.Choices is { Count: > 0 } choices) sb.Append(choices[0]);
                else if (isDef)
                {
                    _active.Add(number);
                    Render(n.Content!, sb, main);
                    _active.Remove(number);
                }
                else sb.Append(Flat(number));
                Check(sb);
                if (!main) continue;
                if (number == 0)
                {
                    if (_final < 0) _final = start;
                    continue;
                }
                if (!_spans.TryGetValue(number, out var list)) _spans[number] = list = [];
                if (isDef && n.Content is not null && HasNumbered(n.Content))
                {
                    _primary[number] = list.Count;
                    _locked.Add(number);
                }
                else if (transform is null && !_locked.Contains(number)) _primary.TryAdd(number, list.Count);
                list.Add(new SnippetSpan(start, sb.Length - start, transform));
            }
        }

        // ---- reading the template ----

        private void Flush(List<Node> nodes, StringBuilder lit)
        {
            if (lit.Length == 0) return;
            nodes.Add(new Node { Literal = lit.ToString() });
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
        private List<Node> Nodes(bool inDefault)
        {
            var nodes = new List<Node>();
            var lit = new StringBuilder();
            while (_i < t.Length)
            {
                if (++_steps > _stepBudget) throw new SnippetLimitException("The snippet is too tangled to read.");
                char c = t[_i];
                if (inDefault && c == '}') break;
                if (c == '\\' && _i + 1 < t.Length && t[_i + 1] is '$' or '}' or '\\') { lit.Append(t[_i + 1]); _i += 2; }
                else if (c == '$' && Dollar(nodes, lit)) { }
                else if (c is '\r' or '\n')
                {
                    if (c == '\r' && _i + 1 < t.Length && t[_i + 1] == '\n') _i++;
                    lit.Append(newLine);
                    _i++;
                }
                else { lit.Append(c); _i++; }
            }
            Flush(nodes, lit);
            return nodes;
        }

        /// <summary>Reads a default that starts at <paramref name="from"/> up to its closing brace, which is left at <c>_i</c>; null when it is not closed or stands too deep.</summary>
        private List<Node>? Default(int from)
        {
            if (_depth >= limits.MaxDepth) return null;
            _i = from;
            _depth++;
            var nodes = Nodes(inDefault: true);
            _depth--;
            return _i < t.Length && t[_i] == '}' ? nodes : null;
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
        private bool Dollar(List<Node> nodes, StringBuilder lit)
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
                if (!braced) return Stop(nodes, lit, number, end);
                if (end >= t.Length) return false;
                if (t[end] == '}') return Stop(nodes, lit, number, end + 1);
                if (t[end] == ':')
                {
                    if (Default(end + 1) is { } content) return Stop(nodes, lit, number, _i + 1, content);
                    return Fail(start, missingBefore);
                }
                if (t[end] == '|' && Choices(end + 1) is { } choices && choices.next < t.Length && t[choices.next] == '}')
                    return Stop(nodes, lit, number, choices.next + 1, choices: choices.items);
                if (t[end] == '/' && Transform(end, out var transform, out int after)) return Stop(nodes, lit, number, after, transform: transform);
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
            if (t[nameEnd] == '}') { Variable(nodes, lit, name, null, null); _i = nameEnd + 1; return true; }
            if (t[nameEnd] == ':')
            {
                if (Default(nameEnd + 1) is { } fallback)
                {
                    Variable(nodes, lit, name, fallback, null);
                    _i++;
                    return true;
                }
            }
            else if (t[nameEnd] == '/' && Transform(nameEnd, out var transform, out int after))
            {
                Variable(nodes, lit, name, null, transform);
                _i = after;
                return true;
            }
            return Fail(start, missingBefore);
        }

        private bool Fail(int start, int missingBefore)
        {
            _i = start;
            _missing.RemoveRange(missingBefore, _missing.Count - missingBefore);
            return false;
        }

        private bool Stop(List<Node> nodes, StringBuilder lit, int number, int next, List<Node>? content = null, List<string>? choices = null, SnippetTransform? transform = null)
        {
            Flush(nodes, lit);
            nodes.Add(new Node { Number = number, Content = content, Choices = choices, Transform = transform });
            _i = next;
            return true;
        }

        private void Variable(List<Node> nodes, StringBuilder lit, string name, List<Node>? fallback, SnippetTransform? transform)
        {
            if (variable?.Invoke(name) is { } value) Append(lit, transform is null ? value : Transformed(transform, value));
            else if (fallback is not null)
            {
                Flush(nodes, lit);
                nodes.AddRange(fallback);
            }
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

        // ---- transforms ----

        /// <summary>
        /// Reads <c>/regex/format/options}</c> with <paramref name="at"/> on the first slash; <paramref name="next"/> is the position after the closing brace.
        /// A slash in the regex is written <c>\/</c>; in the format <c>\/</c>, <c>\$</c>, <c>\\</c> and <c>\}</c> stand for the character itself.
        /// False when it is not closed, the regex is invalid or an option is unknown.
        /// </summary>
        private bool Transform(int at, out SnippetTransform? transform, out int next)
        {
            transform = null;
            next = at;
            var pattern = new StringBuilder();
            int j = at + 1;
            for (; j < t.Length && t[j] != '/'; j++)
            {
                if (t[j] == '\\' && j + 1 < t.Length)
                {
                    if (t[j + 1] != '/') pattern.Append('\\');
                    pattern.Append(t[++j]);
                }
                else pattern.Append(t[j]);
            }
            if (j >= t.Length) return false;

            var format = new List<FormatPart>();
            var text = new StringBuilder();
            j++;
            while (j < t.Length && t[j] != '/')
            {
                if (t[j] == '\\' && j + 1 < t.Length && t[j + 1] is '/' or '$' or '\\' or '}') { text.Append(t[j + 1]); j += 2; }
                else if (t[j] == '$' && FormatGroup(ref j, out var part))
                {
                    if (text.Length > 0) format.Add(new FormatPart(text.ToString()));
                    text.Clear();
                    format.Add(part!);
                }
                else { text.Append(t[j]); j++; }
            }
            if (j >= t.Length) return false;
            if (text.Length > 0) format.Add(new FormatPart(text.ToString()));

            int close = ReadWhile(j + 1, char.IsAsciiLetter);
            if (close >= t.Length || t[close] != '}') return false;
            transform = SnippetTransform.TryCreate(pattern.ToString(), format, t.Substring(j + 1, close - j - 1), limits.TransformTimeout);
            next = close + 1;
            return transform is not null;
        }

        private static readonly string[] CaseModifiers = ["upcase", "downcase", "capitalize", "camelcase", "pascalcase"];

        /// <summary>Reads <c>$1</c>, <c>${1}</c>, <c>${1:/upcase}</c>, <c>${1:+if}</c>, <c>${1:?if:else}</c>, <c>${1:-else}</c> or <c>${1:else}</c> at <paramref name="j"/> (on the dollar sign); false leaves it where it was.</summary>
        private bool FormatGroup(ref int j, out FormatPart? part)
        {
            part = null;
            int a = j + 1;
            if (a >= t.Length) return false;
            bool braced = t[a] == '{';
            if (braced) a++;
            int end = ReadWhile(a, char.IsAsciiDigit);
            if (end == a || end - a > 6 || !int.TryParse(t.AsSpan(a, end - a), out int group)) return false;
            if (!braced) { part = new FormatPart(Group: group); j = end; return true; }
            if (end >= t.Length) return false;
            if (t[end] == '}') { part = new FormatPart(Group: group); j = end + 1; return true; }
            if (t[end] != ':' || end + 1 >= t.Length) return false;

            int c = end + 1;
            if (t[c] == '/')
            {
                int close = t.IndexOf('}', c);
                if (close > 0 && Array.IndexOf(CaseModifiers, t.Substring(c + 1, close - c - 1)) >= 0)
                {
                    part = new FormatPart(Group: group, Case: t.Substring(c + 1, close - c - 1));
                    j = close + 1;
                    return true;
                }
            }
            char kind = t[c] is '+' or '?' or '-' ? t[c] : ' ';
            if (!BraceText(kind == ' ' ? c : c + 1, kind == '?', out string first, out string? second, out int after)) return false;
            part = kind switch
            {
                '+' => new FormatPart(Group: group, If: first),
                '?' => new FormatPart(Group: group, If: first, Else: second ?? ""),
                _ => new FormatPart(Group: group, Else: first),
            };
            j = after;
            return true;
        }

        /// <summary>Reads text up to an unescaped <c>}</c> (<c>\}</c>, <c>\\</c>, <c>\$</c> and <c>\:</c> are the characters themselves); with <paramref name="split"/> the first unescaped colon starts <paramref name="second"/>.</summary>
        private bool BraceText(int from, bool split, out string first, out string? second, out int after)
        {
            var sb = new StringBuilder();
            string? one = null;
            first = "";
            second = null;
            after = from;
            for (int j = from; j < t.Length; j++)
            {
                char ch = t[j];
                if (ch == '\\' && j + 1 < t.Length && t[j + 1] is '}' or '\\' or '$' or ':') sb.Append(t[++j]);
                else if (ch == ':' && split && one is null) { one = sb.ToString(); sb.Clear(); }
                else if (ch == '}')
                {
                    if (one is null) first = sb.ToString();
                    else (first, second) = (one, sb.ToString());
                    after = j + 1;
                    return true;
                }
                else sb.Append(ch);
            }
            return false;
        }
    }
}

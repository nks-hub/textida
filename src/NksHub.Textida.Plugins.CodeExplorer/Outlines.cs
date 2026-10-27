using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.CodeExplorer;

/// <summary>One symbol found in a text: UTF-16 offsets of the name, and the index of the enclosing symbol in the same list (-1 at top level). A parent always comes before its children.</summary>
public readonly record struct Sym(string Kind, string Name, int Start, int End, int Parent);

/// <summary>
/// Outlines from simple per-language rules, no language server and no project index: the text is first masked (comments and strings become blanks, so a brace or a keyword in
/// them is never read) and then scanned for the declarations of the language. Anything the rules do not understand is skipped, never guessed.
/// </summary>
public static partial class Outlines
{
    /// <summary>The language modes that have an outline.</summary>
    public static bool Supports(string language) => language is "html" or "php" or "css" or "csharp" or "markdown";

    public static List<Sym> Extract(string language, string text) => language switch
    {
        "html" => Html(text),
        "php" => Php(text),
        "css" => Css(Mask(text, new MaskRules(false, false, true, false, false))),
        "csharp" => CSharp(Mask(text, new MaskRules(true, false, true, true, false))),
        "markdown" => Markdown(text),
        _ => [],
    };

    // ---- masking ----

    private readonly record struct MaskRules(bool LineSlash, bool LineHash, bool Block, bool CSharp, bool Php);

    /// <summary>Same length as the text: comments, strings and (in PHP) everything outside the PHP tags are replaced by spaces, line breaks stay.</summary>
    private static string Mask(string t, MaskRules r)
    {
        var o = t.ToCharArray();
        int n = t.Length, i = 0;
        bool code = !r.Php;
        void Blank(int from, int to)
        {
            for (int k = from; k < to; k++)
                if (o[k] is not ('\n' or '\r')) o[k] = ' ';
        }
        bool LineStart(int at)
        {
            for (int k = at - 1; k >= 0 && t[k] != '\n'; k--)
                if (t[k] is not (' ' or '\t')) return false;
            return true;
        }
        while (i < n)
        {
            if (!code)
            {
                int open = t.IndexOf("<?", i, StringComparison.Ordinal);
                if (open < 0) { Blank(i, n); break; }
                int length = string.Compare(t, open, "<?php", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 ? 5 : open + 2 < n && t[open + 2] == '=' ? 3 : 2;
                Blank(i, open + length);
                i = open + length;
                code = true;
                continue;
            }
            char c = t[i];
            char next = i + 1 < n ? t[i + 1] : '\0';
            if (r.Php && c == '?' && next == '>') { code = false; i += 2; continue; }
            if ((r.LineSlash && c == '/' && next == '/') || (r.LineHash && c == '#' && next != '[') || (r.CSharp && c == '#' && LineStart(i)))
            {
                int end = i;
                while (end < n && t[end] != '\n' && !(r.Php && t[end] == '?' && end + 1 < n && t[end + 1] == '>')) end++;
                Blank(i, end);
                i = end;
                continue;
            }
            if (r.Block && c == '/' && next == '*')
            {
                int end = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                Blank(i, end);
                i = end;
                continue;
            }
            if (c is '"' or '\'' or '`' && (c != '`' || r.Php))
            {
                int end;
                if (r.CSharp && c == '"' && next == '"' && i + 2 < n && t[i + 2] == '"')
                {
                    int close = t.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    end = close < 0 ? n : close + 3;
                }
                else
                {
                    bool verbatim = r.CSharp && c == '"' && i > 0 && (t[i - 1] == '@' || (t[i - 1] == '$' && i > 1 && t[i - 2] == '@'));
                    end = i + 1;
                    while (end < n)
                    {
                        if (verbatim) { if (t[end] == '"') { if (end + 1 < n && t[end + 1] == '"') { end += 2; continue; } break; } }
                        else if (t[end] == '\\') { end += 2; continue; }
                        else if (t[end] == c) break;
                        else if (t[end] == '\n' && !r.Php) break;
                        end++;
                    }
                    end = Math.Min(n, end + 1);
                }
                Blank(i, end);
                i = end;
                continue;
            }
            i++;
        }
        return new string(o);
    }

    private static string Collapse(string text, int max)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.Length <= max ? sb.ToString() : sb.ToString(0, max) + "…";
    }

    private static (int Start, int End) Trim(string t, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(t[start])) start++;
        while (end > start && char.IsWhiteSpace(t[end - 1])) end--;
        return (start, end);
    }

    // ---- Markdown ----

    [GeneratedRegex(@"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.Compiled)]
    private static partial Regex AtxHeading();

    private static List<Sym> Markdown(string t)
    {
        var result = new List<Sym>();
        var open = new Stack<(int Level, int Index)>();
        char fence = '\0';
        int fenceLength = 0, pos = 0, line = 0;
        while (pos < t.Length)
        {
            int eol = t.IndexOf('\n', pos);
            int next = eol < 0 ? t.Length : eol + 1;
            int end = eol < 0 ? t.Length : eol;
            if (end > pos && t[end - 1] == '\r') end--;
            var text = t.AsSpan(pos, end - pos);
            var trimmed = text.TrimStart(' ');
            if (line == 0 && text.TrimEnd() is "---")
            {
                int close = t.IndexOf("\n---", next - 1, StringComparison.Ordinal);
                if (close >= 0)
                {
                    int closeEnd = t.IndexOf('\n', close + 1);
                    pos = closeEnd < 0 ? t.Length : closeEnd + 1;
                    line = 1;
                    continue;
                }
            }
            line++;
            if (text.Length - trimmed.Length <= 3 && trimmed.Length >= 3 && trimmed[0] is '`' or '~')
            {
                int run = 0;
                while (run < trimmed.Length && trimmed[run] == trimmed[0]) run++;
                if (fence == '\0' && run >= 3) { fence = trimmed[0]; fenceLength = run; }
                else if (fence == trimmed[0] && run >= fenceLength && trimmed[run..].Trim().Length == 0) fence = '\0';
            }
            else if (fence == '\0' && AtxHeading().Match(t, pos, end - pos) is { Success: true } m && m.Groups[2].Length > 0)
            {
                int level = m.Groups[1].Length;
                while (open.Count > 0 && open.Peek().Level >= level) open.Pop();
                result.Add(new Sym("h" + level, m.Groups[2].Value, m.Groups[2].Index, m.Groups[2].Index + m.Groups[2].Length, open.Count > 0 ? open.Peek().Index : -1));
                open.Push((level, result.Count - 1));
            }
            pos = next;
        }
        return result;
    }

    // ---- CSS ----

    private static List<Sym> Css(string t)
    {
        var result = new List<Sym>();
        var scopes = new Stack<int>();
        int prelude = -1, parens = 0;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '(') parens++;
            else if (c == ')' && parens > 0) parens--;
            else if (c == '{' && parens == 0)
            {
                var (s, e) = prelude < 0 ? (0, 0) : Trim(t, prelude, i);
                if (e > s)
                {
                    result.Add(new Sym(t[s] == '@' ? "at-rule" : "rule", Collapse(t[s..e], 400), s, e, scopes.Count > 0 ? scopes.Peek() : -1));
                    scopes.Push(result.Count - 1);
                }
                else scopes.Push(scopes.Count > 0 ? scopes.Peek() : -1);
                prelude = -1;
            }
            else if (c == '}' && parens == 0)
            {
                if (scopes.Count > 0) scopes.Pop();
                prelude = -1;
            }
            else if (c == ';' && parens == 0) prelude = -1;
            else if (prelude < 0 && !char.IsWhiteSpace(c)) prelude = i;
        }
        return result;
    }

    // ---- HTML ----

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> Landmarks = new(StringComparer.OrdinalIgnoreCase) { "main", "nav", "header", "footer", "section", "article", "aside", "form" };

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.';

    /// <summary>Headings, landmarks and elements with an id, nested by the elements that are open around them.</summary>
    private static List<Sym> Html(string t)
    {
        var result = new List<Sym>();
        var stack = new List<(string Tag, int Symbol)>();
        int i = 0, n = t.Length;
        while (i < n)
        {
            int lt = t.IndexOf('<', i);
            if (lt < 0 || lt + 1 >= n) break;
            if (string.CompareOrdinal(t, lt, "<!--", 0, 4) == 0)
            {
                int end = t.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? n : end + 3;
                continue;
            }
            char first = t[lt + 1];
            if (first is '!' or '?')
            {
                int end = t.IndexOf('>', lt + 2);
                i = end < 0 ? n : end + 1;
                continue;
            }
            bool closing = first == '/';
            int nameStart = lt + (closing ? 2 : 1), nameEnd = nameStart;
            if (nameStart >= n || !char.IsLetter(t[nameStart])) { i = lt + 1; continue; }
            while (nameEnd < n && IsNameChar(t[nameEnd])) nameEnd++;
            string tag = t[nameStart..nameEnd];

            // the end of the tag, outside the quotes of its attributes
            int gt = nameEnd;
            char quote = '\0';
            for (; gt < n; gt++)
            {
                char c = t[gt];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '"' or '\'') quote = c;
                else if (c == '>') break;
            }
            if (gt >= n) break;
            i = gt + 1;
            if (closing)
            {
                for (int k = stack.Count - 1; k >= 0; k--)
                    if (string.Equals(stack[k].Tag, tag, StringComparison.OrdinalIgnoreCase)) { stack.RemoveRange(k, stack.Count - k); break; }
                continue;
            }

            string? id = AttributeValue(t, nameEnd, gt, "id");
            bool heading = tag.Length == 2 && (tag[0] is 'h' or 'H') && tag[1] is >= '1' and <= '6';
            int symbol = -1;
            if (heading || id is { Length: > 0 } || Landmarks.Contains(tag))
            {
                string name, kind = heading ? tag.ToLowerInvariant() : Landmarks.Contains(tag) ? "landmark" : "element";
                if (heading)
                {
                    int textEnd = t.IndexOf('<', i);
                    name = Collapse(t[i..(textEnd < 0 ? n : textEnd)], 400);
                    if (name.Length == 0) name = id is { Length: > 0 } ? tag.ToLowerInvariant() + "#" + id : tag.ToLowerInvariant();
                }
                else name = id is { Length: > 0 } ? tag.ToLowerInvariant() + "#" + id : tag.ToLowerInvariant();
                int parent = -1;
                for (int k = stack.Count - 1; k >= 0 && parent < 0; k--) parent = stack[k].Symbol;
                result.Add(new Sym(kind, name, lt, gt + 1, parent));
                symbol = result.Count - 1;
            }
            bool selfClosing = t[gt - 1] == '/' || VoidElements.Contains(tag);
            if (!selfClosing) stack.Add((tag, symbol));
            if (!selfClosing && (tag.Equals("script", StringComparison.OrdinalIgnoreCase) || tag.Equals("style", StringComparison.OrdinalIgnoreCase)))
            {
                int close = t.IndexOf("</" + tag, i, StringComparison.OrdinalIgnoreCase);
                i = close < 0 ? n : close;
            }
        }
        return result;
    }

    private static string? AttributeValue(string t, int from, int to, string name)
    {
        int i = from;
        while (i < to)
        {
            while (i < to && (char.IsWhiteSpace(t[i]) || t[i] == '/')) i++;
            int s = i;
            while (i < to && !char.IsWhiteSpace(t[i]) && t[i] is not ('=' or '/')) i++;
            string attribute = t[s..i];
            while (i < to && char.IsWhiteSpace(t[i])) i++;
            string? value = null;
            if (i < to && t[i] == '=')
            {
                i++;
                while (i < to && char.IsWhiteSpace(t[i])) i++;
                if (i < to && t[i] is '"' or '\'')
                {
                    int close = t.IndexOf(t[i], i + 1, to - i - 1);
                    if (close < 0) close = to;
                    value = t[(i + 1)..close];
                    i = close + 1;
                }
                else
                {
                    int vs = i;
                    while (i < to && !char.IsWhiteSpace(t[i])) i++;
                    value = t[vs..i];
                }
            }
            if (attribute.Equals(name, StringComparison.OrdinalIgnoreCase)) return value;
        }
        return null;
    }

    // ---- PHP ----

    [GeneratedRegex(@"\b(?:(class|interface|trait|enum)\s+([A-Za-z_]\w*)|function\s*&?\s*([A-Za-z_]\w*)\s*\(|namespace\s+([A-Za-z_][\w\\]*)\s*(?=[;{]))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PhpDeclaration();

    private static Dictionary<int, int> BracePairs(string t)
    {
        var pairs = new Dictionary<int, int>();
        var open = new Stack<int>();
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '{') open.Push(i);
            else if (t[i] == '}' && open.Count > 0) pairs[open.Pop()] = i;
        }
        return pairs;
    }

    private static int MatchingParen(string t, int open)
    {
        int depth = 0;
        for (int i = open; i < t.Length; i++)
        {
            if (t[i] == '(') depth++;
            else if (t[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static List<Sym> Php(string source)
    {
        var t = Mask(source, new MaskRules(true, true, true, false, true));
        var pairs = BracePairs(t);
        var result = new List<Sym>();
        var stack = new List<(int Symbol, int BodyEnd, string Kind)>();
        foreach (Match m in PhpDeclaration().Matches(t))
        {
            int before = m.Index - 1;
            while (before >= 0 && char.IsWhiteSpace(t[before])) before--;
            if (before >= 0 && t[before] is '>' or ':') continue; // ::class, ->function
            while (stack.Count > 0 && stack[^1].BodyEnd < m.Index) stack.RemoveAt(stack.Count - 1);

            string kind, name;
            Group nameGroup;
            int bodyEnd = -1;
            if (m.Groups[4].Success)
            {
                kind = "namespace";
                nameGroup = m.Groups[4];
                if (stack.Count > 0 && stack[^1].Kind == "namespace") stack.RemoveAt(stack.Count - 1);
                int after = nameGroup.Index + nameGroup.Length;
                while (after < t.Length && char.IsWhiteSpace(t[after])) after++;
                bodyEnd = t[after] == '{' && pairs.TryGetValue(after, out var close) ? close : t.Length;
                name = nameGroup.Value;
            }
            else if (m.Groups[2].Success)
            {
                kind = m.Groups[1].Value.ToLowerInvariant();
                nameGroup = m.Groups[2];
                name = nameGroup.Value;
                if (name.Equals("extends", StringComparison.OrdinalIgnoreCase) || name.Equals("implements", StringComparison.OrdinalIgnoreCase)) continue;
                int brace = t.IndexOf('{', nameGroup.Index + nameGroup.Length);
                bodyEnd = brace >= 0 && pairs.TryGetValue(brace, out var close) ? close : -1;
            }
            else
            {
                nameGroup = m.Groups[3];
                int open = m.Index + m.Length - 1, close = MatchingParen(t, open);
                kind = stack.Count > 0 && stack[^1].Kind is "class" or "interface" or "trait" or "enum" ? "method" : "function";
                name = nameGroup.Value + "(" + (close < 0 ? "" : Collapse(t[(open + 1)..close], 60)) + ")";
                if (close >= 0)
                {
                    int k = close + 1;
                    while (k < t.Length && t[k] is not ('{' or ';')) k++;
                    bodyEnd = k < t.Length && t[k] == '{' && pairs.TryGetValue(k, out var body) ? body : -1;
                }
            }
            result.Add(new Sym(kind, name, nameGroup.Index, nameGroup.Index + nameGroup.Length, stack.Count > 0 ? stack[^1].Symbol : -1));
            if (bodyEnd >= 0) stack.Add((result.Count - 1, bodyEnd, kind));
        }
        return result;
    }

    // ---- C# ----

    private readonly record struct Tok(int Start, int End);

    private sealed class Scope(string kind, int symbol, string name, bool inline = false, bool fileScoped = false)
    {
        public string Kind { get; } = kind; // namespace, type, enum, body
        public int Symbol { get; } = symbol;
        public string Name { get; } = name;
        public bool Inline { get; } = inline;
        public bool FileScoped { get; } = fileScoped;
    }

    private static readonly HashSet<string> TypeKeywords = ["class", "struct", "interface", "enum", "record"];
    private static readonly HashSet<string> NotNames = ["where", "new", "this", "base", "return", "if", "while", "for", "foreach", "switch", "lock", "using", "catch", "fixed"];

    private static List<Sym> CSharp(string t)
    {
        var result = new List<Sym>();
        var scopes = new List<Scope> { new("namespace", -1, "") };
        var header = new List<Tok>();
        int parens = 0;
        string Text(Tok k) => t.Substring(k.Start, k.End - k.Start);
        bool IsIdent(Tok k) => char.IsLetter(t[k.Start]) || t[k.Start] is '_' or '@';
        bool IsPunct(Tok k, char c) => k.End - k.Start == 1 && t[k.Start] == c;
        int Parent() => scopes[^1].Symbol;

        // an '=' outside parentheses starts an initializer or an expression body ('==', '<=', '>=' and '!=' do not)
        int InitializerAt()
        {
            int depth = 0;
            for (int k = 0; k < header.Count; k++)
            {
                if (IsPunct(header[k], '(')) depth++;
                else if (IsPunct(header[k], ')')) depth--;
                else if (depth == 0 && IsPunct(header[k], '=')
                    && !(k + 1 < header.Count && header[k + 1].Start == header[k].End && IsPunct(header[k + 1], '='))
                    && !(k > 0 && header[k - 1].End == header[k].Start && (IsPunct(header[k - 1], '=') || IsPunct(header[k - 1], '<') || IsPunct(header[k - 1], '>') || IsPunct(header[k - 1], '!'))))
                    return k;
            }
            return -1;
        }

        int Add(string kind, string name, int start, int end)
        {
            result.Add(new Sym(kind, name, start, end, Parent()));
            return result.Count - 1;
        }

        // the type declared by the header (index of its name token), or -1
        int TypeNameAt(out string kind)
        {
            kind = "";
            for (int k = 0; k + 1 < header.Count; k++)
            {
                if (IsPunct(header[k], '(') || IsPunct(header[k], '=') || IsPunct(header[k], ':')) return -1;
                string word = Text(header[k]);
                if (!IsIdent(header[k]) || !TypeKeywords.Contains(word)) continue;
                if (word == "record" && Text(header[k + 1]) is "class" or "struct" && k + 2 < header.Count) { kind = "record"; return k + 2; }
                if (IsIdent(header[k + 1]) && !NotNames.Contains(Text(header[k + 1]))) { kind = word; return k + 1; }
            }
            return -1;
        }

        // the member declared by the header: the name before the first '(' (generic arguments skipped), or the last identifier of a property
        (string Kind, int Name, int Open)? Member(bool hasBody)
        {
            int depth = 0, open = -1;
            for (int k = 0; k < header.Count; k++)
            {
                if (IsPunct(header[k], '(')) { if (depth++ == 0) { open = k; break; } }
            }
            if (open >= 0)
            {
                int name = open - 1;
                if (name >= 0 && IsPunct(header[name], '>'))
                {
                    int angle = 0;
                    for (; name >= 0; name--)
                    {
                        if (IsPunct(header[name], '>')) angle++;
                        else if (IsPunct(header[name], '<') && --angle == 0) { name--; break; }
                    }
                }
                if (name < 0 || !IsIdent(header[name]) || NotNames.Contains(Text(header[name]))) return null;
                string word = Text(header[name]);
                bool delegateType = header.Any(h => IsIdent(h) && Text(h) == "delegate");
                return (delegateType ? "delegate" : word == scopes[^1].Name ? "constructor" : "method", name, open);
            }
            if (header.Count == 0 || !IsIdent(header[^1]) || NotNames.Contains(Text(header[^1]))) return null;
            return header.Count >= 2 || Text(header[^1]) == "this" ? ("property", header.Count - 1, -1) : null;
        }

        string Signature(int name, int open)
        {
            string text = Text(header[name]);
            if (open < 0) return text;
            int depth = 0, close = -1;
            for (int k = open; k < header.Count; k++)
            {
                if (IsPunct(header[k], '(')) depth++;
                else if (IsPunct(header[k], ')') && --depth == 0) { close = k; break; }
            }
            int end = close < 0 ? header[^1].End : header[close].Start;
            return text + "(" + Collapse(t[header[open].End..end], 60) + ")";
        }

        int i = 0, n = t.Length;
        while (i < n)
        {
            char c = t[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsLetter(c) || c is '_' or '@')
            {
                int s = i++;
                while (i < n && (char.IsLetterOrDigit(t[i]) || t[i] == '_')) i++;
                if (scopes[^1].Kind is not "body" and not "enum") header.Add(new Tok(s, i));
                continue;
            }
            int at = i++;
            var top = scopes[^1];
            if (c == '[')
            {
                int depth = 1;
                while (i < n && depth > 0) { if (t[i] == '[') depth++; else if (t[i] == ']') depth--; i++; }
                continue;
            }
            if (c == '{')
            {
                if (top.Kind is "body" or "enum") { scopes.Add(new Scope("body", -1, "")); continue; }
                bool inline = parens > 0 || InitializerAt() >= 0;
                if (inline) { scopes.Add(new Scope("body", -1, "", inline: true)); continue; }
                int typeName = TypeNameAt(out var typeKind);
                int nsAt = header.FindIndex(h => IsIdent(h) && Text(h) == "namespace");
                if (nsAt >= 0 && nsAt + 1 < header.Count)
                {
                    int sym = Add("namespace", t[header[nsAt + 1].Start..header[^1].End].Replace(" ", ""), header[nsAt + 1].Start, header[^1].End);
                    scopes.Add(new Scope("namespace", sym, ""));
                }
                else if (typeName >= 0)
                {
                    string name = Text(header[typeName]);
                    int sym = Add(typeKind, name, header[typeName].Start, header[typeName].End);
                    scopes.Add(new Scope(typeKind == "enum" ? "enum" : "type", sym, name));
                }
                else if (top.Kind == "type" && Member(true) is { } member)
                {
                    Add(member.Kind, Signature(member.Name, member.Open), header[member.Name].Start, header[member.Name].End);
                    scopes.Add(new Scope("body", -1, ""));
                }
                else scopes.Add(new Scope("body", -1, ""));
                header.Clear();
                parens = 0;
                continue;
            }
            if (c == '}')
            {
                if (top.FileScoped) continue;
                if (scopes.Count > 1) scopes.RemoveAt(scopes.Count - 1);
                if (!top.Inline && scopes[^1].Kind is not "body" and not "enum") { header.Clear(); parens = 0; }
                continue;
            }
            if (top.Kind is "body" or "enum") continue;
            if (c == '(') parens++;
            else if (c == ')' && parens > 0) parens--;
            if (c == ';' && parens == 0)
            {
                if (header.Count > 0 && !(IsIdent(header[0]) && Text(header[0]) == "using"))
                {
                    int nsAt = header.FindIndex(h => IsIdent(h) && Text(h) == "namespace");
                    int typeName = TypeNameAt(out var typeKind);
                    if (nsAt == 0 && header.Count > 1)
                    {
                        int sym = Add("namespace", t[header[1].Start..header[^1].End].Replace(" ", ""), header[1].Start, header[^1].End);
                        scopes.Add(new Scope("namespace", sym, "", fileScoped: true));
                    }
                    else if (typeName >= 0 && typeKind == "record") Add("record", Text(header[typeName]), header[typeName].Start, header[typeName].End);
                    else if (top.Kind == "type")
                    {
                        int init = InitializerAt();
                        var member = Member(false);
                        if (init < 0 && member is { Open: >= 0 } declared)
                            Add(declared.Kind, Signature(declared.Name, declared.Open), header[declared.Name].Start, header[declared.Name].End);
                        else if (init >= 0 && t[header[init].End] == '>') // expression body: a method when a '(' comes first, otherwise a property
                        {
                            if (member is { Open: >= 0 } body && body.Open < init)
                                Add(body.Kind, Signature(body.Name, body.Open), header[body.Name].Start, header[body.Name].End);
                            else if (init > 0 && IsIdent(header[init - 1]) && !NotNames.Contains(Text(header[init - 1])))
                                Add("property", Text(header[init - 1]), header[init - 1].Start, header[init - 1].End);
                        }
                    }
                }
                header.Clear();
                continue;
            }
            header.Add(new Tok(at, i));
        }
        return result;
    }
}

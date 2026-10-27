using System.Text;

namespace NksHub.Textida.Plugins.Emmet;

/// <param name="Indent">The text one level of nesting is written as (a tab, or spaces).</param>
/// <param name="BaseIndent">The indentation of the line the expansion starts on; every further line of the expansion begins with it.</param>
/// <param name="MaxChars">Longest expansion; a longer one is refused.</param>
/// <param name="MaxCarets">Most places Tab visits in one expansion; the first ones are kept.</param>
public sealed record ExpandOptions(string Indent, string BaseIndent, int MaxChars, int MaxCarets);

/// <summary>
/// The text an abbreviation expands to (lines separated by <c>\n</c>) and the UTF-16 offsets of the places Tab visits, in this order: first the
/// <paramref name="Carets"/> (the end of the text when there is no empty place, which is no place at all), then the <paramref name="Stops"/> (null when there are none).
/// </summary>
public sealed record Expansion(string Text, IReadOnlyList<int> Carets, IReadOnlyList<int>? Stops = null)
{
    /// <summary>The places Tab visits, at most <paramref name="max"/> of them; none when the expansion has no empty place.</summary>
    public IReadOnlyList<int> Places(int max) => [.. Carets.Concat(Stops ?? []).Where(at => at < Text.Length).Take(max)];

    /// <summary>
    /// The expansion as a snippet template of the editor: <c>$</c> and <c>\</c> escaped, <c>${1}</c>, <c>${2}</c>... where Tab stops. An expansion with places is
    /// padded with stops at its end up to <paramref name="count"/>, because the snippets of several carets are walked through together.
    /// </summary>
    public string Template(int max, int count)
    {
        var places = Places(max);
        var marks = places.Select((at, i) => (At: at, Number: i + 1)).ToList();
        if (places.Count > 0)
            for (int n = places.Count + 1; n <= count; n++) marks.Add((Text.Length, n));
        var sb = new StringBuilder(Text.Length + 8 * marks.Count);
        int from = 0;
        foreach (var (at, number) in marks.OrderBy(m => m.At).ThenBy(m => m.Number))
        {
            Escape(sb, from, at);
            sb.Append("${").Append(number).Append('}');
            from = at;
        }
        Escape(sb, from, Text.Length);
        return sb.ToString();
    }

    private void Escape(StringBuilder sb, int from, int to)
    {
        foreach (var c in Text.AsSpan(from, to - from))
        {
            if (c is '$' or '\\') sb.Append('\\');
            sb.Append(c);
        }
    }
}

/// <summary>
/// Expands HTML abbreviations. Layout rule (this editor's own, close to what Emmet users expect): the top level and the children of an element that has
/// a block-level child are written one per line, indented one level; an element whose children are all inline stays on one line with them.
/// Tab visits the empty non-void elements first, in the order of the text; when there is none, the first empty attribute value; then the other empty
/// attribute values. Without any empty place the caret is at the end.
/// </summary>
public static class HtmlExpander
{
    private static readonly HashSet<string> Inline = new(StringComparer.Ordinal)
    {
        "a", "abbr", "acronym", "b", "bdi", "bdo", "big", "br", "button", "cite", "code", "data", "dfn", "em", "font", "i", "img", "input", "kbd", "label",
        "map", "mark", "object", "output", "picture", "q", "ruby", "s", "samp", "script", "select", "small", "span", "strike", "strong", "sub", "sup",
        "textarea", "time", "tt", "u", "var", "wbr",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "a", "abbr", "address", "area", "article", "aside", "audio", "b", "base", "bdi", "bdo", "blockquote", "body", "br", "button", "canvas", "caption", "cite",
        "code", "col", "colgroup", "data", "datalist", "dd", "del", "details", "dfn", "dialog", "div", "dl", "dt", "em", "embed", "fieldset", "figcaption",
        "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "i", "iframe", "img", "input", "ins", "kbd",
        "label", "legend", "li", "link", "main", "map", "mark", "menu", "meta", "meter", "nav", "noscript", "object", "ol", "optgroup", "option", "output", "p",
        "param", "picture", "pre", "progress", "q", "rp", "rt", "ruby", "s", "samp", "script", "search", "section", "select", "slot", "small", "source", "span",
        "strong", "style", "sub", "summary", "sup", "svg", "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track",
        "u", "ul", "var", "video", "wbr",
    };

    private static readonly string[] SubmitLike = ["submit", "button", "reset"];
    private static readonly string[] NamedInputs = ["text", "search", "email", "url", "password", "number", "tel", "date", "color", "range", "hidden", "file", "checkbox", "radio"];

    private static readonly Dictionary<string, (string Tag, (string Name, string Value)[] Attributes)> Aliases = BuildAliases();

    private static Dictionary<string, (string, (string, string)[])> BuildAliases()
    {
        var map = new Dictionary<string, (string, (string, string)[])>(StringComparer.Ordinal)
        {
            ["a"] = ("a", [("href", "")]),
            ["a:link"] = ("a", [("href", "http://")]),
            ["a:mail"] = ("a", [("href", "mailto:")]),
            ["a:tel"] = ("a", [("href", "tel:+")]),
            ["acr"] = ("abbr", [("title", "")]),
            ["abbr"] = ("abbr", [("title", "")]),
            ["adr"] = ("address", []),
            ["bq"] = ("blockquote", []),
            ["btn"] = ("button", []),
            ["btn:b"] = ("button", [("type", "button")]),
            ["btn:r"] = ("button", [("type", "reset")]),
            ["btn:s"] = ("button", [("type", "submit")]),
            ["emb"] = ("embed", [("src", ""), ("type", "")]),
            ["fig"] = ("figure", []),
            ["figc"] = ("figcaption", []),
            ["form"] = ("form", [("action", "")]),
            ["form:get"] = ("form", [("action", ""), ("method", "get")]),
            ["form:post"] = ("form", [("action", ""), ("method", "post")]),
            ["ifr"] = ("iframe", [("src", "")]),
            ["img"] = ("img", [("src", ""), ("alt", "")]),
            ["inp"] = ("input", [("type", "text")]),
            ["input"] = ("input", [("type", "text")]),
            ["label"] = ("label", [("for", "")]),
            ["link"] = ("link", [("rel", "stylesheet"), ("href", "")]),
            ["link:css"] = ("link", [("rel", "stylesheet"), ("href", "style.css")]),
            ["meta:utf"] = ("meta", [("charset", "UTF-8")]),
            ["meta:vp"] = ("meta", [("name", "viewport"), ("content", "width=device-width, initial-scale=1.0")]),
            ["obj"] = ("object", [("data", ""), ("type", "")]),
            ["opt"] = ("option", [("value", "")]),
            ["option"] = ("option", [("value", "")]),
            ["optg"] = ("optgroup", []),
            ["prog"] = ("progress", []),
            ["script:src"] = ("script", [("src", "")]),
            ["select"] = ("select", [("name", ""), ("id", "")]),
            ["src"] = ("source", [("src", ""), ("type", "")]),
            ["str"] = ("strong", []),
            ["tarea"] = ("textarea", [("name", ""), ("id", ""), ("cols", "30"), ("rows", "10")]),
            ["textarea"] = ("textarea", [("name", ""), ("id", ""), ("cols", "30"), ("rows", "10")]),
        };
        foreach (var type in NamedInputs) map["input:" + type] = ("input", [("type", type), ("name", ""), ("id", "")]);
        foreach (var type in SubmitLike) map["input:" + type] = ("input", [("type", type), ("value", "")]);
        map["input:image"] = ("input", [("type", "image"), ("src", ""), ("alt", "")]);
        return map;
    }

    private const string DocumentSkeleton = "<!DOCTYPE html>\n<html lang=\"\">\n<head>\n{i}<meta charset=\"UTF-8\">\n{i}<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n{i}<title></title>\n</head>\n<body>\n{i}\n</body>\n</html>";

    /// <summary>True when the text is only a tag name that is known (so a plain word is not turned into an element by Tab unless it is one).</summary>
    private static bool IsKnownName(string name) => Known.Contains(name) || Aliases.ContainsKey(name);

    /// <summary>The expansion of an abbreviation, or null when the text is not one. Throws <see cref="ExpansionTooLargeException"/> when it would be longer than the limit.</summary>
    public static Expansion? Expand(string abbreviation, ExpandOptions options)
    {
        if (abbreviation is "!" or "html:5") return Skeleton(options);
        try { return Render(AbbreviationParser.Parse(abbreviation), options, null); }
        catch (AbbreviationException) { return null; }
    }

    /// <summary>
    /// Wraps <paramref name="content"/> in the abbreviation: the content goes into the innermost last element. A bare <c>*</c> on that element repeats it for every
    /// non-blank line. Null when the text is not an abbreviation or cannot hold the content (a void element would lose it).
    /// </summary>
    public static Expansion? Wrap(string abbreviation, string content, ExpandOptions options)
    {
        try
        {
            var roots = AbbreviationParser.Parse(abbreviation);
            var target = roots[^1];
            while (target.Children.Count > 0) target = target.Children[^1];
            if (target.IsGroup) return null;
            return Render(roots, options, new Wrapped(target, content));
        }
        catch (AbbreviationException) { return null; }
    }

    private static Expansion Skeleton(ExpandOptions o)
    {
        string text = DocumentSkeleton.Replace("{i}", o.Indent).Replace("\n", "\n" + o.BaseIndent);
        if (text.Length > o.MaxChars) throw new ExpansionTooLargeException();
        int caret = text.IndexOf("<body>", StringComparison.Ordinal) + "<body>".Length + 1 + o.BaseIndent.Length + o.Indent.Length;
        return new Expansion(text, [caret], [text.IndexOf("</title>", StringComparison.Ordinal)]);
    }

    private sealed record Wrapped(Node Target, string Content)
    {
        public List<string> Lines { get; } = [.. Content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')];
    }

    private readonly record struct Frame(int Index, int Count);

    private static Expansion Render(List<Node> roots, ExpandOptions options, Wrapped? wrap)
    {
        if (roots.Count == 0) throw new AbbreviationException("Empty.");
        if (wrap is null && roots.Count == 1 && roots[0] is { IsGroup: false, Children.Count: 0, Text.Length: 0, Attributes.Count: 0, Repeat: 1, ImplicitRepeat: false } bare && !IsKnownName(bare.Name!))
            throw new AbbreviationException("A plain word is not an abbreviation unless it is a tag name.");
        var renderer = new Renderer(options, wrap);
        renderer.Roots(roots);
        return renderer.Result();
    }

    private sealed class Renderer
    {
        private readonly ExpandOptions _o;
        private readonly Wrapped? _wrap;
        private readonly StringBuilder _sb = new();
        private readonly List<int> _carets = [];
        private readonly List<int> _emptyAttributes = [];

        public Renderer(ExpandOptions o, Wrapped? wrap)
        {
            _o = o;
            _wrap = wrap;
            if (wrap is not null)
            {
                var lines = wrap.Lines;
                if (wrap.Target.ImplicitRepeat)
                {
                    var kept = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    lines.Clear();
                    lines.AddRange(kept);
                }
                else
                {
                    while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
                    while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
                    for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].TrimEnd();
                }
                if (lines.Count == 0) throw new AbbreviationException("Nothing to wrap.");
            }
        }

        public Expansion Result()
        {
            List<int> carets = _wrap is not null ? [_sb.Length] : _carets.Count > 0 ? _carets : _emptyAttributes.Count > 0 ? [_emptyAttributes[0]] : [_sb.Length];
            List<int> stops = [.. _emptyAttributes.Where(at => !carets.Contains(at))];
            return new Expansion(_sb.ToString(), Cap(carets), Cap(stops));
        }

        private List<int> Cap(List<int> offsets) => offsets.Count <= _o.MaxCarets ? offsets : offsets.GetRange(0, _o.MaxCarets);

        private void NewLine(int level)
        {
            _sb.Append('\n').Append(_o.BaseIndent);
            for (int i = 0; i < level; i++) _sb.Append(_o.Indent);
        }

        private void Guard()
        {
            if (_sb.Length > _o.MaxChars) throw new ExpansionTooLargeException();
        }

        public void Roots(List<Node> roots)
        {
            bool first = true;
            Siblings(roots, null, 0, broken: true, new Frame(1, 1), ref first, atRoot: true);
        }

        private void Siblings(List<Node> nodes, string? parent, int level, bool broken, Frame inherited, ref bool first, bool atRoot)
        {
            foreach (var node in nodes)
            {
                bool target = _wrap is not null && ReferenceEquals(node, _wrap.Target);
                int count = node.ImplicitRepeat ? (_wrap is not null && target ? _wrap.Lines.Count : throw new AbbreviationException("A bare * needs text to wrap.")) : node.Repeat;
                bool repeats = node.ImplicitRepeat || node.Repeat > 1;
                for (int i = 1; i <= count; i++)
                {
                    var frame = repeats ? new Frame(i, count) : inherited;
                    if (node.IsGroup) Siblings(node.Children, parent, level, broken, frame, ref first, atRoot);
                    else Element(node, parent, level, broken, frame, ref first, atRoot, target ? i : 0);
                    Guard();
                }
            }
        }

        private string TagOf(Node node, string? parent, Frame frame)
        {
            string name = Number(node.Name!, frame);
            if (name.Length == 0) name = ImplicitName(parent);
            if (Aliases.TryGetValue(name, out var alias)) return alias.Tag;
            if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':')))
                throw new AbbreviationException("Not a tag name.");
            return name;
        }

        private static string ImplicitName(string? parent) => parent switch
        {
            "ul" or "ol" or "menu" => "li",
            "table" or "thead" or "tbody" or "tfoot" => "tr",
            "tr" => "td",
            "select" or "optgroup" or "datalist" => "option",
            null => "div",
            _ => Inline.Contains(parent) || parent is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre" or "dt" ? "span" : "div",
        };

        private bool HasBlockChild(Node parent, string parentTag)
        {
            foreach (var child in parent.Children)
            {
                if (child.IsGroup)
                {
                    if (HasBlockChild(child, parentTag)) return true;
                    continue;
                }
                string name = child.Name!;
                string tag = name.Length == 0 ? ImplicitName(parentTag) : Aliases.TryGetValue(name, out var alias) ? alias.Tag : name;
                if (!Inline.Contains(tag)) return true;
            }
            return false;
        }

        private void Element(Node node, string? parent, int level, bool broken, Frame frame, ref bool first, bool atRoot, int wrapIndex)
        {
            string tag = TagOf(node, parent, frame);
            bool target = wrapIndex > 0;
            if (target && Void.Contains(tag)) throw new AbbreviationException("A void element cannot hold text.");
            if (broken && !(first && atRoot)) NewLine(level);
            first = false;

            _sb.Append('<').Append(tag);
            WriteAttributes(node, tag, frame);
            _sb.Append('>');
            if (Void.Contains(tag))
            {
                if (node.Children.Count > 0) throw new AbbreviationException("A void element has no children.");
                return;
            }

            string text = Number(node.Text, frame);
            bool contentLines = false;
            List<string>? lines = null;
            if (target)
            {
                if (node.ImplicitRepeat) text += _wrap!.Lines[wrapIndex - 1];
                else if (_wrap!.Lines.Count == 1) text += _wrap.Lines[0];
                else { contentLines = true; lines = _wrap.Lines; }
            }

            bool blockChildren = node.Children.Count > 0 && HasBlockChild(node, tag);
            if (node.Children.Count == 0 && !contentLines)
            {
                _sb.Append(text);
                if (text.Length == 0) _carets.Add(_sb.Length);
            }
            else if (!blockChildren && !contentLines)
            {
                _sb.Append(text);
                bool inner = true;
                Siblings(node.Children, tag, level + 1, broken: false, frame, ref inner, atRoot: false);
            }
            else
            {
                _sb.Append(text);
                if (contentLines)
                {
                    foreach (var line in lines!)
                    {
                        NewLine(level + 1);
                        _sb.Append(line);
                    }
                }
                bool inner = false;
                Siblings(node.Children, tag, level + 1, broken: true, frame, ref inner, atRoot: false);
                NewLine(level);
            }
            _sb.Append("</").Append(tag).Append('>');
        }

        private void WriteAttributes(Node node, string tag, Frame frame)
        {
            var attributes = new List<(string Name, string Value)>();
            string name = Number(node.Name!, frame);
            if (name.Length == 0) name = tag;
            if (Aliases.TryGetValue(name, out var alias)) attributes.AddRange(alias.Attributes);
            foreach (var (attribute, value) in node.Attributes)
            {
                string n = Number(attribute, frame), v = Number(value, frame);
                int at = attributes.FindIndex(a => a.Name == n);
                if (at >= 0 && n != "class") attributes[at] = (n, v);
                else if (at >= 0) attributes[at] = (n, attributes[at].Value + " " + v);
                else attributes.Add((n, v));
            }
            foreach (var (attribute, value) in attributes)
            {
                _sb.Append(' ').Append(attribute).Append("=\"");
                if (value.Length == 0) _emptyAttributes.Add(_sb.Length);
                _sb.Append(value.Replace("\"", "&quot;")).Append('"');
            }
        }
    }

    /// <summary>
    /// Replaces the item numbering in a name, class or text: <c>$</c> is the number of the repeat (more dollar signs pad with zeros), <c>$@-</c> counts backwards,
    /// <c>$@N</c> starts at N and <c>$@-N</c> counts down ending at N. A backslash before a dollar sign keeps it.
    /// </summary>
    private static string Number(string s, Frame frame)
    {
        if (s.IndexOf('$') < 0) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && s[i + 1] == '$') { sb.Append('$'); i++; continue; }
            if (c != '$') { sb.Append(c); continue; }
            int width = 1;
            while (i + 1 < s.Length && s[i + 1] == '$') { width++; i++; }
            bool reverse = false;
            int start = 1;
            if (i + 1 < s.Length && s[i + 1] == '@')
            {
                int j = i + 2;
                if (j < s.Length && s[j] == '-') { reverse = true; j++; }
                int digits = j;
                while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
                if (j > digits && int.TryParse(s.AsSpan(digits, j - digits), out var parsed)) start = parsed;
                i = j - 1;
            }
            long value = reverse ? start + frame.Count - frame.Index : start + frame.Index - 1;
            sb.Append(value.ToString().PadLeft(width, '0'));
        }
        return sb.ToString();
    }
}

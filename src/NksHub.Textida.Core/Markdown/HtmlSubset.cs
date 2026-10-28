using System.Net;
using System.Text;

namespace NksHub.Textida.Core.Markdown;

/// <summary>
/// A node of the HTML the preview understands. A text node has no <see cref="Tag"/>. A <see cref="Literal"/> text node is the source of something the subset does not render
/// (a tag it does not know, a stray end tag, a comment, a whole <c>script</c> element); the preview shows it as written.
/// </summary>
public sealed class HtmlNode
{
    /// <summary>The lower-case tag name; null for text.</summary>
    public string? Tag { get; init; }

    public string Text { get; init; } = "";

    public bool Literal { get; init; }

    /// <summary>Zero-based line of the source where the node starts.</summary>
    public int Line { get; init; }

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<HtmlNode> Children { get; } = [];

    public string Attribute(string name) => Attributes.GetValueOrDefault(name, "");

    /// <summary>The text of the node and everything below it, as written.</summary>
    public string TextContent()
    {
        if (Tag is null) return Text;
        var sb = new StringBuilder();
        Append(this, sb);
        return sb.ToString();

        static void Append(HtmlNode node, StringBuilder into)
        {
            if (node.Tag is null) into.Append(node.Text);
            else foreach (var child in node.Children) Append(child, into);
        }
    }
}

/// <summary>
/// A small, safe reader of HTML for the preview (P9.3.1): headings, paragraphs, lists, tables, code, links, images and emphasis become nodes; every other tag, a stray end tag, a comment and
/// the whole content of <c>script</c> and <c>style</c> stay in the tree as literal text. Attributes are kept as text and never acted on here, so nothing can run or be fetched
/// through the reader. The implied end tags of <c>p</c>, <c>li</c>, <c>tr</c>, <c>td</c>, <c>th</c> and of the table sections are inferred.
/// </summary>
public static class HtmlSubset
{
    private static readonly HashSet<string> Rendered = new(StringComparer.Ordinal)
    {
        "h1", "h2", "h3", "h4", "h5", "h6", "p", "ul", "ol", "li", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "pre", "blockquote", "hr",
        "a", "img", "em", "i", "strong", "b", "code", "br",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "br", "hr", "img" };

    /// <summary>Tags that end an open <c>p</c> when they start.</summary>
    private static readonly HashSet<string> EndsParagraph = new(StringComparer.Ordinal) { "h1", "h2", "h3", "h4", "h5", "h6", "p", "ul", "ol", "table", "pre", "blockquote", "hr" };

    private static readonly HashSet<string> Sections = new(StringComparer.Ordinal) { "thead", "tbody", "tfoot" };

    /// <summary>Tags that lay out blocks; everything else the subset renders is text-level.</summary>
    public static bool IsBlock(string tag) => tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "ul" or "ol" or "table" or "pre" or "blockquote" or "hr";

    /// <param name="maxNodes">Nodes after which the rest of the text is kept as one literal node.</param>
    /// <param name="maxDepth">Nesting beyond which a tag is kept as literal text.</param>
    public static IReadOnlyList<HtmlNode> Parse(string html, int maxNodes, int maxDepth)
    {
        var root = new HtmlNode { Tag = "#root" };
        var stack = new List<HtmlNode> { root };
        int count = 0, lineAt = 0, line = 0;

        int LineOf(int position)
        {
            for (; lineAt < position; lineAt++) if (html[lineAt] == '\n') line++;
            return line;
        }

        // consecutive plain text is collected here and becomes one node when the structure changes, so joining it costs nothing per piece
        var pending = new StringBuilder();
        int pendingLine = 0;

        void Flush()
        {
            if (pending.Length == 0) return;
            stack[^1].Children.Add(new HtmlNode { Text = pending.ToString(), Line = pendingLine });
            pending.Clear();
            count++;
        }

        void Add(HtmlNode node)
        {
            Flush();
            stack[^1].Children.Add(node);
            count++;
        }

        void Text(string text, int at, bool literal)
        {
            if (text.Length == 0) return;
            if (literal)
            {
                Add(new HtmlNode { Text = text, Literal = true, Line = LineOf(at) });
                return;
            }
            if (pending.Length == 0) pendingLine = LineOf(at);
            pending.Append(WebUtility.HtmlDecode(text));
        }

        int Open(string name)
        {
            for (int k = stack.Count - 1; k > 0; k--)
                if (stack[k].Tag == name) return k;
            return -1;
        }

        void CloseTo(int index)
        {
            Flush();
            if (index > 0) stack.RemoveRange(index, stack.Count - index);
        }

        // closes what the new tag ends without an end tag of its own, looking up to the nearest container only
        void ImplyEnds(string name)
        {
            if (EndsParagraph.Contains(name) && stack[^1].Tag == "p")
            {
                Flush();
                stack.RemoveAt(stack.Count - 1);
            }
            switch (name)
            {
                case "li": CloseWithin("li", "ul", "ol"); break;
                case "tr": CloseWithin("tr", "table"); break;
                case "td" or "th": CloseWithin("td", "tr", "table"); CloseWithin("th", "tr", "table"); break;
                case "thead" or "tbody" or "tfoot": CloseWithin("tr", "table"); foreach (var s in Sections) CloseWithin(s, "table"); break;
            }
        }

        void CloseWithin(string tag, params string[] boundaries)
        {
            for (int k = stack.Count - 1; k > 0; k--)
            {
                if (stack[k].Tag == tag) { CloseTo(k); return; }
                if (stack[k].Tag is { } open && boundaries.Contains(open)) return;
            }
        }

        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0) { Text(html[i..], i, false); break; }
            Text(html[i..lt], i, false);
            i = lt;
            if (count >= maxNodes) { Text(html[i..], i, true); break; }
            if (!ReadTag(html, i, out var tag)) { Text("<", i, false); i++; continue; }
            if (tag.End < 0) { Text(html[i..], i, true); break; } // a tag the text never closes
            string source = html[i..tag.End];
            if (!Rendered.Contains(tag.Name))
            {
                int end = tag.End;
                if (!tag.Closing && tag.Name is "script" or "style")
                {
                    int close = html.IndexOf("</" + tag.Name, end, StringComparison.OrdinalIgnoreCase);
                    int closeEnd = close < 0 ? -1 : html.IndexOf('>', close);
                    end = closeEnd < 0 ? html.Length : closeEnd + 1;
                }
                Text(html[i..end], i, true);
                i = end;
                continue;
            }
            if (tag.Closing)
            {
                int k = Void.Contains(tag.Name) ? -1 : Open(tag.Name);
                if (k < 0) Text(source, i, true);
                else CloseTo(k);
            }
            else if (stack.Count > maxDepth) Text(source, i, true);
            else
            {
                ImplyEnds(tag.Name);
                var node = new HtmlNode { Tag = tag.Name, Line = LineOf(i) };
                foreach (var (name, value) in tag.Attributes) node.Attributes[name] = value;
                Add(node);
                if (!Void.Contains(tag.Name)) stack.Add(node);
            }
            i = tag.End;
        }
        Flush();
        return root.Children;
    }

    private sealed record TagRead(string Name, bool Closing, int End, List<(string Name, string Value)> Attributes);

    /// <summary>Reads the tag at <paramref name="at"/> (the <c>&lt;</c>). False when it is not a tag; <c>End</c> is -1 when the tag never ends.</summary>
    private static bool ReadTag(string s, int at, out TagRead tag)
    {
        tag = null!;
        int p = at + 1;
        bool closing = p < s.Length && s[p] == '/';
        if (closing) p++;
        if (p >= s.Length || !char.IsAsciiLetter(s[p]))
        {
            // a comment, a doctype or any other markup declaration is kept as written up to its end
            if (closing || p >= s.Length || s[p] is not ('!' or '?')) return false;
            bool comment = string.CompareOrdinal(s, at, "<!--", 0, 4) == 0;
            int found = comment ? s.IndexOf("-->", at + 4, StringComparison.Ordinal) : s.IndexOf('>', p);
            tag = new TagRead("#markup", false, found < 0 ? -1 : found + (comment ? 3 : 1), []);
            return true;
        }
        int nameStart = p;
        while (p < s.Length && (char.IsAsciiLetterOrDigit(s[p]) || s[p] is '-' or ':' or '_')) p++;
        string name = s[nameStart..p].ToLowerInvariant();
        var attributes = new List<(string, string)>();
        while (p < s.Length)
        {
            while (p < s.Length && (char.IsWhiteSpace(s[p]) || s[p] == '/')) p++;
            if (p >= s.Length) break;
            if (s[p] == '>') { tag = new TagRead(name, closing, p + 1, attributes); return true; }
            int attrStart = p;
            while (p < s.Length && !char.IsWhiteSpace(s[p]) && s[p] is not ('=' or '>' or '/')) p++;
            string attr = s[attrStart..p];
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            string value = "";
            if (p < s.Length && s[p] == '=')
            {
                p++;
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
                if (p < s.Length && s[p] is '"' or '\'')
                {
                    int close = s.IndexOf(s[p], p + 1);
                    if (close < 0) break;
                    value = s[(p + 1)..close];
                    p = close + 1;
                }
                else
                {
                    int valueStart = p;
                    while (p < s.Length && !char.IsWhiteSpace(s[p]) && s[p] != '>') p++;
                    value = s[valueStart..p];
                }
            }
            if (attr.Length > 0) attributes.Add((attr, WebUtility.HtmlDecode(value)));
        }
        tag = new TagRead(name, closing, -1, attributes);
        return true;
    }
}

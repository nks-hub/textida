using System.Text;

namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// Tags of an HTML text as offsets, for closing the open tag, jumping to the partner tag, selecting or removing an element and stripping tags.
/// Comments and the content of <c>script</c> and <c>style</c> hold no tags; a tag ends at the first <c>&gt;</c> outside quotes.
/// shortcut: the whole text is scanned for each command and the optional end tags of HTML (<c>li</c>, <c>p</c>) are not inferred; a paged document needs checkpoints (P3).
/// </summary>
public static class HtmlTags
{
    /// <summary>A tag at <see cref="Start"/> (the <c>&lt;</c>) up to <see cref="End"/> (exclusive). <see cref="SelfClosed"/> is also true for void tags such as <c>br</c>.</summary>
    public readonly record struct Tag(int Start, int End, string Name, bool Closing, bool SelfClosed);

    /// <summary>An opening tag with its end tag; <see cref="Close"/> is null for a void or self-closed tag and for one never closed.</summary>
    public readonly record struct Element(Tag Open, Tag? Close)
    {
        public int Start => Open.Start;
        public int End => Close?.End ?? Open.End;
    }

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    public static List<Tag> Scan(string text)
    {
        var tags = new List<Tag>();
        int i = 0;
        while (i < text.Length && (i = text.IndexOf('<', i)) >= 0)
        {
            if (SkipComment(text, i) is var after and > 0) { i = after; continue; }
            bool closing = i + 1 < text.Length && text[i + 1] == '/';
            int nameStart = i + (closing ? 2 : 1), n = nameStart;
            while (n < text.Length && IsNameChar(text[n])) n++;
            if (n == nameStart || !char.IsLetter(text[nameStart])) { i++; continue; }
            int end = TagEnd(text, n);
            if (end < 0) break;
            string name = text[nameStart..n];
            bool self = !closing && (text[end - 2] == '/' || VoidTags.Contains(name));
            tags.Add(new Tag(i, end, name, closing, self));
            i = end;
            if (!closing && !self && (name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase)))
            {
                int raw = text.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                if (raw < 0) break;
                i = raw;
            }
        }
        return tags;
    }

    /// <summary>Pairs the tags by nesting; an end tag closes the nearest open tag of its name and ends the ones opened inside it without a partner. An end tag without an open tag is ignored.</summary>
    public static List<Element> Elements(IEnumerable<Tag> tags)
    {
        var result = new List<Element>();
        var stack = new List<Tag>();
        foreach (var t in tags)
        {
            if (t.SelfClosed) result.Add(new Element(t, null));
            else if (!t.Closing) stack.Add(t);
            else
            {
                int k = stack.FindLastIndex(o => o.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
                if (k < 0) continue;
                for (int j = stack.Count - 1; j > k; j--) result.Add(new Element(stack[j], null));
                result.Add(new Element(stack[k], t));
                stack.RemoveRange(k, stack.Count - k);
            }
        }
        result.AddRange(stack.Select(o => new Element(o, null)));
        return result;
    }

    /// <summary>The name of the nearest tag before <paramref name="caret"/> that is still open, or null.</summary>
    public static string? UnclosedBefore(string text, int caret) =>
        Elements(Scan(text).Where(t => t.End <= caret)).Where(e => e.Close is null && !e.Open.SelfClosed).OrderByDescending(e => e.Start).Select(e => e.Open.Name).FirstOrDefault();

    /// <summary>The start of the partner of the tag that holds <paramref name="caret"/> (inside it or right behind it); false when the caret is in no tag or the tag has no partner.</summary>
    public static bool TryMatch(string text, int caret, out int partner)
    {
        partner = -1;
        var e = Elements(Scan(text)).Where(e => e.Close is not null && (Holds(e.Open, caret) || Holds(e.Close.Value, caret))).OrderByDescending(e => e.Start).FirstOrDefault();
        if (e.Close is not { } close) return false;
        partner = Holds(e.Open, caret) ? close.Start : e.Open.Start;
        return true;
    }

    /// <summary>The smallest element with an end tag that contains the range and is larger than it; selecting again with the result grows the selection by one level.</summary>
    public static bool TryElement(string text, int start, int end, out int elementStart, out int elementEnd)
    {
        elementStart = elementEnd = -1;
        var found = Elements(Scan(text)).Where(e => e.Close is not null && e.Start <= start && end <= e.End && (e.Start, e.End) != (start, end)).OrderBy(e => e.End - e.Start).ToList();
        if (found.Count == 0) return false;
        (elementStart, elementEnd) = (found[0].Start, found[0].End);
        return true;
    }

    /// <summary>The tags to remove for the element that holds <paramref name="caret"/> (its start tag and its end tag, or the one tag); the content stays. Ordered from the end of the text.</summary>
    public static IReadOnlyList<Tag> TagsToRemove(string text, int caret)
    {
        var e = Elements(Scan(text)).Where(e => e.Start <= caret && caret <= e.End).OrderBy(e => e.End - e.Start).FirstOrDefault();
        if (e.Open.Name is null) return [];
        return e.Close is { } close ? [close, e.Open] : [e.Open];
    }

    /// <summary>The text without its tags and comments; the content of <c>script</c> and <c>style</c> stays as text.</summary>
    public static string Strip(string text)
    {
        var sb = new StringBuilder(text.Length);
        int last = 0;
        for (int i = 0; i < text.Length && (i = text.IndexOf('<', i)) >= 0;)
        {
            int end = SkipComment(text, i);
            if (end <= 0 && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] is '/' or '!' or '?'))
                end = text[i + 1] is '!' or '?' ? text.IndexOf('>', i) + 1 : TagEnd(text, i + 1);
            if (end <= 0) { i++; continue; }
            sb.Append(text, last, i - last);
            last = i = end;
        }
        return sb.Append(text, last, text.Length - last).ToString();
    }

    public static bool IsName(string name) => name.Length > 0 && char.IsLetter(name[0]) && name.All(IsNameChar);

    private static bool Holds(Tag t, int caret) => t.Start <= caret && caret <= t.End;

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '-' or ':' or '_' or '.';

    /// <summary>The offset after a comment that starts at <paramref name="at"/> (the text end when unterminated), or 0 when none starts there.</summary>
    private static int SkipComment(string text, int at)
    {
        if (string.CompareOrdinal(text, at, "<!--", 0, 4) != 0) return 0;
        int e = text.IndexOf("-->", at + 4, StringComparison.Ordinal);
        return e < 0 ? text.Length : e + 3;
    }

    /// <summary>The offset after the <c>&gt;</c> that ends the tag whose name ends at <paramref name="from"/>, or -1 when it never ends.</summary>
    private static int TagEnd(string text, int from)
    {
        char quote = '\0';
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '>') return i + 1;
        }
        return -1;
    }
}

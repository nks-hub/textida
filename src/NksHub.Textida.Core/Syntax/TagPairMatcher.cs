using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// Finds the partner of a Latte block tag: <c>{if ...}</c> and <c>{/if}</c>, <c>{foreach ...}</c> and <c>{/foreach}</c>, <c>{/}</c> closing the innermost one.
/// Tags inside a comment do not count; a tag whose body ends with <c>/</c> (<c>{label x /}</c>) has no partner.
/// shortcut: a tag ends at the first <c>}</c> of its line and the whole document is scanned for each jump, like <see cref="BracketMatcher"/>; a paged document needs checkpoints (P3).
/// </summary>
public static partial class TagPairMatcher
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "if", "ifset", "ifchanged", "ifcontent", "foreach", "for", "while", "iterateWhile", "switch", "block", "define", "capture",
        "snippet", "snippetArea", "cache", "first", "last", "sep", "try", "spaceless", "syntax", "embed", "form", "formContainer",
        "label", "translate",
    };

    [GeneratedRegex(@"\{(/?)([A-Za-z_][\w]*)?")]
    private static partial Regex TagStart();

    private readonly record struct Tag(int Line, int Start, int End, string? Name, bool Closing);

    /// <summary>
    /// The position of the <c>{</c> of the partner of the tag that holds the caret (inside it or right behind its <c>}</c>);
    /// null when the caret is in no block tag or the tag has no partner.
    /// </summary>
    public static TextPosition? FindPair(IReadOnlyList<string> lines, Tokenizer? tokenizer, int line, int column)
    {
        var tokens = new BracketMatcher.LineTokens(lines, tokenizer);
        var stack = new List<Tag>();
        Tag? caretTag = null, partner = null;
        for (int l = 0; l < lines.Count; l++)
        {
            foreach (var tag in TagsOf(lines[l], l))
            {
                if (tokens.IsComment(l, tag.Start)) continue;
                Tag? other = null;
                if (!tag.Closing) stack.Add(tag);
                else if (Pop(stack, tag.Name) is { } open) other = open;
                else continue;

                bool here = l == line && column >= tag.Start && column <= tag.End + 1;
                if (here) caretTag = tag;
                if (other is { } o)
                {
                    if (here) partner = o;
                    else if (caretTag is { } c && c == o) partner = tag;
                }
            }
        }
        return partner is { } p ? new TextPosition(p.Line, p.Start) : null;
    }

    private static Tag? Pop(List<Tag> stack, string? name)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (name is not null && stack[i].Name != name) continue;
            var open = stack[i];
            stack.RemoveRange(i, stack.Count - i);
            return open;
        }
        return null;
    }

    private static IEnumerable<Tag> TagsOf(string text, int line)
    {
        foreach (Match m in TagStart().Matches(text))
        {
            bool closing = m.Groups[1].Length > 0;
            string? name = m.Groups[2].Success ? m.Groups[2].Value : null;
            if (closing ? name is null && !(m.Index + 2 < text.Length && text[m.Index + 2] == '}') : name is null || !BlockTags.Contains(name)) continue;
            int close = text.IndexOf('}', m.Index);
            int end = close < 0 ? text.Length : close;
            if (!closing && close > 0 && text[close - 1] == '/') continue;
            yield return new Tag(line, m.Index, end, name, closing);
        }
    }
}

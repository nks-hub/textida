using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>How comments are written in a language: a line comment prefix and whether <c>/* */</c> blocks continue with a star.</summary>
public sealed record CommentStyle(string? Line, bool StarBlock)
{
    public static readonly CommentStyle None = new(null, false);
}

/// <summary>Auto-closing of brackets and quotes. Pure text edits; the caller wraps one call in one undo step.</summary>
public static class PairedCharacters
{
    private const string Brackets = "([{";
    private const string Closers = ")]}";
    private const string Quotes = "\"'`";

    private static char CloserOf(char open) => open switch { '(' => ')', '[' => ']', '{' => '}', _ => open };

    /// <summary>
    /// Handles one typed character. Returns false when the editor should insert it as usual.
    /// A bracket or quote wraps a selection; otherwise it inserts the pair when the next character is blank, the end or a closer.
    /// A closer or quote that is already there is typed over. An apostrophe after a letter or digit is plain text.
    /// </summary>
    public static bool TryType(TextDocument d, Sel sel, char typed, out Sel result)
    {
        result = sel;
        bool opener = Brackets.Contains(typed) || Quotes.Contains(typed);
        bool closer = Closers.Contains(typed) || Quotes.Contains(typed);
        if (!opener && !closer) return false;

        if (!sel.IsEmpty)
        {
            if (!opener) return false;
            string inner = d.GetText(sel.Start, sel.Length);
            d.Replace(sel.Start, sel.Length, typed + inner + CloserOf(typed));
            result = new Sel(sel.Start + 1, sel.Start + 1 + inner.Length);
            return true;
        }

        int at = sel.Caret;
        char next = at < d.TextLength ? d.GetCharAt(at) : '\0';
        char prev = at > 0 ? d.GetCharAt(at - 1) : '\0';
        if (closer && next == typed && (Closers.Contains(typed) || prev != typed || Quotes.Contains(typed)))
        {
            result = Sel.At(at + 1);
            return true;
        }

        if (!opener) return false;
        if (Quotes.Contains(typed) && (char.IsLetterOrDigit(prev) || prev == typed || (next != '\0' && !char.IsWhiteSpace(next) && !Closers.Contains(next)))) return false;
        if (Brackets.Contains(typed) && next != '\0' && !char.IsWhiteSpace(next) && !Closers.Contains(next) && !Quotes.Contains(next)) return false;
        d.Insert(at, typed.ToString() + CloserOf(typed));
        result = Sel.At(at + 1);
        return true;
    }

    /// <summary>Backspace between an opening and its closing character removes both.</summary>
    public static bool TryBackspace(TextDocument d, Sel sel, out Sel result)
    {
        result = sel;
        int at = sel.Caret;
        if (!sel.IsEmpty || at == 0 || at >= d.TextLength) return false;
        char before = d.GetCharAt(at - 1), after = d.GetCharAt(at);
        bool pair = (Brackets.Contains(before) && CloserOf(before) == after) || (Quotes.Contains(before) && before == after);
        if (!pair) return false;
        d.Remove(at - 1, 2);
        result = Sel.At(at - 1);
        return true;
    }
}

/// <summary>Enter inside a comment continues it on the next line.</summary>
public static class CommentContinuation
{
    /// <summary>
    /// Inserts a line break plus the comment lead of the current line (<c>// </c>, <c># </c>, <c> * </c>). A line holding only the
    /// lead ends the comment: the lead is removed and a plain line break follows. Returns false when the line is no comment.
    /// </summary>
    public static bool TryContinue(TextDocument d, Sel sel, CommentStyle style, string newLine, out Sel result)
    {
        result = sel;
        if (!sel.IsEmpty) return false;
        var line = d.GetLineByOffset(sel.Caret);
        string text = d.GetText(line.Offset, line.Length);
        int indent = 0;
        while (indent < text.Length && text[indent] is ' ' or '\t') indent++;
        string rest = text[indent..];
        string? lead = LeadOf(rest, style);
        if (lead is null) return false;
        int leadEnd = line.Offset + indent + lead.Length;
        if (sel.Caret < leadEnd) return false;

        string afterLead = text[(indent + lead.Length)..];
        string pad = text[..indent];
        if (afterLead.Trim().Length == 0 && sel.Caret == line.EndOffset && !rest.StartsWith("/*", StringComparison.Ordinal))
        {
            d.Replace(line.Offset, line.Length, "");
            d.Insert(line.Offset, newLine);
            result = Sel.At(line.Offset + newLine.Length);
            return true;
        }

        string insert = newLine + pad + (rest.StartsWith("/*", StringComparison.Ordinal) ? " * " : lead);
        d.Insert(sel.Caret, insert);
        result = Sel.At(sel.Caret + insert.Length);
        return true;
    }

    private static string? LeadOf(string rest, CommentStyle style)
    {
        if (style.Line is { } prefix && rest.StartsWith(prefix, StringComparison.Ordinal))
        {
            int n = prefix.Length;
            while (n < rest.Length && rest[n] == prefix[^1]) n++; // "###" and "///" stay together
            return rest.Length > n && rest[n] == ' ' ? rest[..(n + 1)] : rest[..n];
        }
        if (!style.StarBlock) return null;
        if (rest.StartsWith("/*", StringComparison.Ordinal) && !rest.Contains("*/", StringComparison.Ordinal)) return rest.StartsWith("/**", StringComparison.Ordinal) ? "/**" : "/*";
        if (rest.StartsWith('*') && !rest.StartsWith("*/", StringComparison.Ordinal)) return rest.Length > 1 && rest[1] == ' ' ? "* " : "*";
        return null;
    }
}

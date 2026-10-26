using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>How comments are written in a language: a line comment prefix and whether <c>/* */</c> blocks continue with a star.</summary>
public sealed record CommentStyle(string? Line, bool StarBlock)
{
    public static readonly CommentStyle None = new(null, false);
}

/// <summary>A replacement of <see cref="Length"/> chars at <see cref="Start"/> in text view offsets.</summary>
public readonly record struct ViewEdit(int Start, int Length, string Text);

/// <summary>Auto-closing of brackets and quotes. Each rule has a <c>TryPlan…</c> form that only describes the edit, so several carets can be combined into one transaction.</summary>
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
        if (!TryPlanType(d, sel, typed, out var edit, out result)) return false;
        Apply(d, edit);
        return true;
    }

    private static void Apply(TextDocument d, ViewEdit edit)
    {
        if (edit.Length > 0 || edit.Text.Length > 0) d.Replace(edit.Start, edit.Length, edit.Text);
    }

    /// <summary>Like <see cref="TryType"/> without changing the document; <paramref name="result"/> is the selection after <paramref name="edit"/> alone.</summary>
    public static bool TryPlanType(TextDocument d, Sel sel, char typed, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = sel;
        bool opener = Brackets.Contains(typed) || Quotes.Contains(typed);
        bool closer = Closers.Contains(typed) || Quotes.Contains(typed);
        if (!opener && !closer) return false;

        if (!sel.IsEmpty)
        {
            if (!opener) return false;
            string inner = d.GetText(sel.Start, sel.Length);
            edit = new(sel.Start, sel.Length, typed + inner + CloserOf(typed));
            result = new Sel(sel.Start + 1, sel.Start + 1 + inner.Length);
            return true;
        }

        int at = sel.Caret;
        char next = at < d.TextLength ? d.GetCharAt(at) : '\0';
        char prev = at > 0 ? d.GetCharAt(at - 1) : '\0';
        if (closer && next == typed && (Closers.Contains(typed) || prev != typed || Quotes.Contains(typed)))
        {
            edit = new(at, 0, "");
            result = Sel.At(at + 1);
            return true;
        }

        if (!opener) return false;
        if (Quotes.Contains(typed) && (char.IsLetterOrDigit(prev) || prev == typed || (next != '\0' && !char.IsWhiteSpace(next) && !Closers.Contains(next)))) return false;
        if (Brackets.Contains(typed) && next != '\0' && !char.IsWhiteSpace(next) && !Closers.Contains(next) && !Quotes.Contains(next)) return false;
        edit = new(at, 0, typed.ToString() + CloserOf(typed));
        result = Sel.At(at + 1);
        return true;
    }

    /// <summary>Backspace between an opening and its closing character removes both.</summary>
    public static bool TryBackspace(TextDocument d, Sel sel, out Sel result)
    {
        if (!TryPlanBackspace(d, sel, out var edit, out result)) return false;
        Apply(d, edit);
        return true;
    }

    public static bool TryPlanBackspace(TextDocument d, Sel sel, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = sel;
        int at = sel.Caret;
        if (!sel.IsEmpty || at == 0 || at >= d.TextLength) return false;
        char before = d.GetCharAt(at - 1), after = d.GetCharAt(at);
        bool pair = (Brackets.Contains(before) && CloserOf(before) == after) || (Quotes.Contains(before) && before == after);
        if (!pair) return false;
        edit = new(at - 1, 2, "");
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
        if (!TryPlan(d, sel, style, newLine, out var edit, out result)) return false;
        d.Replace(edit.Start, edit.Length, edit.Text);
        return true;
    }

    /// <summary>Like <see cref="TryContinue"/> without changing the document; <paramref name="result"/> is the selection after <paramref name="edit"/> alone.</summary>
    public static bool TryPlan(TextDocument d, Sel sel, CommentStyle style, string newLine, out ViewEdit edit, out Sel result)
    {
        edit = default;
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
            edit = new(line.Offset, line.Length, newLine);
            result = Sel.At(line.Offset + newLine.Length);
            return true;
        }

        string insert = newLine + pad + (rest.StartsWith("/*", StringComparison.Ordinal) ? " * " : lead);
        edit = new(sel.Caret, 0, insert);
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

/// <summary>
/// Paired characters the way the original editor does it (the profile "pspad"): a closing character that is already to the right is typed over
/// (it need not have been added by the editor); a pair is added only when nothing but a blank follows, and a quote only when the line is empty
/// or the character before it is no letter or digit and the line is longer than two characters. A selection is replaced, not wrapped.
/// <c>&lt;</c> pairs in markup only.
/// </summary>
public sealed class PspadPairsRule(bool markup) : ITypingRule
{
    private const string Openers = "([{\"'`";
    private const string TypeOver = "\"')>]`}";

    public bool TryPlan(TextDocument d, Sel sel, char typed, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = sel;
        var line = d.GetLineByOffset(sel.Start);
        string rest = d.GetText(sel.End, line.EndOffset - sel.End);
        if (sel.IsEmpty && TypeOver.Contains(typed) && rest.Length > 0 && rest[0] == typed)
        {
            edit = new(sel.Start, 0, "");
            result = Sel.At(sel.Start + 1);
            return true;
        }
        bool bracket = typed is '(' or '[' or '{' || (typed == '<' && markup);
        bool quote = typed is '"' or '\'' or '`';
        if (!bracket && !quote) return false;

        int before = sel.Start - line.Offset;
        int length = before + 1 + rest.Length;
        bool roomAfter = bracket ? rest.Length == 0 || rest[0] == ' ' : rest.Length <= 1 || rest[0] == ' ';
        if (!roomAfter) return false;
        if (quote)
        {
            bool prevIsWord = before >= 1 && char.IsLetterOrDigit(d.GetCharAt(line.Offset + before - 1));
            if (!(length <= 1 || (length >= 3 && !prevIsWord))) return false;
        }
        char closer = typed switch { '(' => ')', '[' => ']', '{' => '}', '<' => '>', _ => typed };
        edit = new(sel.Start, sel.Length, typed.ToString() + closer);
        result = Sel.At(sel.Start + 1);
        return true;
    }
}


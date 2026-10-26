using System.Text;
using AvaloniaEdit.Document;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Tokens of the lines of a document, from the shared token base (<c>Core.Syntax</c>); lines are 1-based like the text view.</summary>
public interface ITokenProbe
{
    IReadOnlyList<Token> TokensOfLine(int line);
}

/// <summary>Which smart keys for web files are on; each one can be switched off in the settings.</summary>
public sealed record SmartKeyOptions(
    bool RenameTag = true, bool CloseTag = true, bool EscapeInString = true, bool JsonComma = true,
    bool PhpArrow = true, bool SelectVariable = true, bool SqlEnd = true)
{
    public static readonly SmartKeyOptions Off = new(false, false, false, false, false, false, false);
}

/// <summary>
/// Smart keys for web files. They look only at the tokens around the caret (no parsing of the code): renaming the partner of an edited
/// tag name, completing <c>&lt;/</c>, escaping pasted text inside a string, the comma of a JSON value on Enter, <c>-&gt;</c> after a PHP
/// variable, selecting a PHP variable without its <c>$</c> and <c>END</c> after SQL <c>BEGIN</c>. Every method only describes an edit.
/// </summary>
public sealed class SmartKeys(TextDocument d, ITokenProbe tokens, string language, SmartKeyOptions options, string indentUnit = "    ") : ITypingRule
{
    private static readonly HashSet<string> VoidTags = new(["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EscapingLanguages = ["json", "javascript", "typescript", "php", "python", "csharp", "java", "cpp", "shell", "html", "xml"];

    private bool Markup => language is "html" or "xml" or "php";

    /// <summary>True for the languages some smart key applies to, so a surface can skip the tokens for the others.</summary>
    public static bool HasRulesFor(string language) => language is "html" or "xml" or "php" or "json" or "sql" || EscapingLanguages.Contains(language);

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is ':' or '_' or '.' or '-';

    private static bool IsString(Token t) => t.Kind.StartsWith("string", StringComparison.Ordinal);

    private static bool IsComment(Token t) => t.Kind.StartsWith("comment", StringComparison.Ordinal);

    private Token? TokenCovering(int line, int column) => tokens.TokensOfLine(line).Cast<Token?>().FirstOrDefault(t => t!.Value.Start <= column && column < t.Value.End);

    private Token? TokenEndingAt(int line, int column) => tokens.TokensOfLine(line).Cast<Token?>().FirstOrDefault(t => t!.Value.End == column);

    private string Text(DocumentLine line, Token t) => d.GetText(line.Offset + t.Start, t.Length);

    private bool InStringOrComment(int offset)
    {
        var line = d.GetLineByOffset(offset);
        return TokenCovering(line.LineNumber, offset - line.Offset) is { } t && (IsString(t) || IsComment(t));
    }

    // ---- typing: </ and -> ----

    /// <inheritdoc />
    public bool TryPlan(TextDocument doc, Sel sel, char typed, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = sel;
        if (!sel.IsEmpty) return false;
        if (typed == '/' && options.CloseTag && Markup && TryCloseTag(sel.Caret, out edit)) { result = Sel.At(sel.Caret + edit.Text.Length); return true; }
        if (typed == '-' && options.PhpArrow && language == "php" && TryArrow(sel.Caret)) { edit = new(sel.Caret, 0, "->"); result = Sel.At(sel.Caret + 2); return true; }
        return false;
    }

    private bool TryCloseTag(int caret, out ViewEdit edit)
    {
        edit = default;
        if (caret == 0 || d.GetCharAt(caret - 1) != '<' || (caret < d.TextLength && d.GetCharAt(caret) == '>') || InStringOrComment(caret - 1)) return false;
        var open = new List<string>();
        foreach (var tag in Tags(caret - 1))
        {
            if (tag.Kind == TagKind.Open) open.Add(tag.Name);
            else if (tag.Kind == TagKind.Close && open.FindLastIndex(n => n.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)) is >= 0 and var at) open.RemoveRange(at, open.Count - at);
        }
        if (open.Count == 0) return false;
        edit = new(caret, 0, "/" + open[^1] + ">");
        return true;
    }

    private bool TryArrow(int caret)
    {
        var line = d.GetLineByOffset(caret);
        int column = caret - line.Offset;
        if (TokenEndingAt(line.LineNumber, column) is not { } t || t.Kind != "variable" || Text(line, t).Length < 2 || Text(line, t)[0] != '$') return false;
        if (caret < d.TextLength && d.GetCharAt(caret) is '>' or '-' or '=') return false;
        // inside a double-quoted string the variable is interpolated: the token before it is part of the string
        if (t.Start > 0 && TokenCovering(line.LineNumber, t.Start - 1) is { } before && IsString(before)) return false;
        return true;
    }

    // ---- tags ----

    private enum TagKind { Open, Close, SelfClosed }

    private readonly record struct Tag(TagKind Kind, string Name, int NameStart);

    /// <summary>
    /// The tags of the document in order, up to <paramref name="before"/> when given; void and self-closed tags are reported as <see cref="TagKind.SelfClosed"/>.
    /// The tokenizer merges neighbouring tag tokens (<c>&lt;a&gt;&lt;/a&gt;</c> is one), so the text of every tag token is split here.
    /// </summary>
    private IEnumerable<Tag> Tags(int before = int.MaxValue)
    {
        string? pending = null;
        int pendingStart = 0;
        for (int n = 1; n <= d.LineCount; n++)
        {
            var line = d.GetLineByNumber(n);
            if (line.Offset > before) yield break;
            foreach (var t in tokens.TokensOfLine(n))
            {
                if (t.Kind != "tag") continue;
                if (line.Offset + t.End > before) yield break;
                string text = Text(line, t);
                int i = 0;
                while (i < text.Length)
                {
                    if (pending is not null)
                    {
                        bool close = text[i] == '>', slash = text[i] == '/' && i + 1 < text.Length && text[i + 1] == '>';
                        if (close || slash)
                        {
                            yield return new(slash || VoidTags.Contains(pending) ? TagKind.SelfClosed : TagKind.Open, pending, pendingStart);
                            i += slash ? 2 : 1;
                        }
                        pending = null;
                        if (close || slash) continue;
                    }
                    if (text[i] != '<' || i + 1 >= text.Length) { i++; continue; }
                    if (text[i + 1] == '/')
                    {
                        string name = NameAt(text, i + 2);
                        if (name.Length > 0) yield return new(TagKind.Close, name, line.Offset + t.Start + i + 2);
                        int gt = text.IndexOf('>', i + 2 + name.Length);
                        i = gt < 0 ? text.Length : gt + 1;
                    }
                    else if (char.IsLetter(text[i + 1]) || text[i + 1] is '_' or ':')
                    {
                        string name = NameAt(text, i + 1);
                        int start = line.Offset + t.Start + i + 1, after = i + 1 + name.Length;
                        int gt = after < text.Length ? text.IndexOf('>', after) : -1;
                        if (gt < 0) { (pending, pendingStart) = (name, start); i = text.Length; continue; }
                        bool self = text[gt - 1] == '/' || VoidTags.Contains(name);
                        yield return new(self ? TagKind.SelfClosed : TagKind.Open, name, start);
                        i = gt + 1;
                    }
                    else i++;
                }
            }
        }
    }

    private static string NameAt(string text, int from)
    {
        int end = from;
        while (end < text.Length && IsNameChar(text[end])) end++;
        return text[from..end];
    }

    // ---- renaming the partner of a tag ----

    /// <summary>
    /// When <paramref name="edit"/> changes only the name of a tag, the edit that gives the matching tag the same new name. The tags are paired by
    /// their nesting in the text as it is before the edit, so a half-typed name still finds its partner. False when the edit is elsewhere, empties the name or no partner exists.
    /// </summary>
    public bool TryPlanRename(ViewEdit edit, out ViewEdit partner)
    {
        partner = default;
        if (!options.RenameTag || !Markup || (edit.Text.Length > 0 && !edit.Text.All(IsNameChar)) || !InsideTagName(edit.Start)) return false;
        var tags = Tags().ToList();
        var pair = new int[tags.Count];
        Array.Fill(pair, -1);
        var open = new List<int>();
        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Kind == TagKind.Open) open.Add(i);
            else if (tags[i].Kind == TagKind.Close && open.FindLastIndex(j => tags[j].Name.Equals(tags[i].Name, StringComparison.OrdinalIgnoreCase)) is >= 0 and var at)
            {
                (pair[open[at]], pair[i]) = (i, open[at]);
                open.RemoveRange(at, open.Count - at);
            }
        }
        for (int i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (edit.Start < tag.NameStart || edit.Start + edit.Length > tag.NameStart + tag.Name.Length) continue;
            string renamed = tag.Name[..(edit.Start - tag.NameStart)] + edit.Text + tag.Name[(edit.Start + edit.Length - tag.NameStart)..];
            if (pair[i] < 0 || renamed.Length == 0) return false;
            var other = tags[pair[i]];
            partner = new(other.NameStart, other.Name.Length, renamed);
            return true;
        }
        return false;
    }

    /// <summary>Cheap check before the tokens are read: only name characters separate <paramref name="offset"/> from a <c>&lt;</c> or <c>&lt;/</c>.</summary>
    private bool InsideTagName(int offset)
    {
        int i = offset;
        while (i > 0 && IsNameChar(d.GetCharAt(i - 1))) i--;
        return i > 0 && (d.GetCharAt(i - 1) == '<' || (d.GetCharAt(i - 1) == '/' && i > 1 && d.GetCharAt(i - 2) == '<'));
    }

    // ---- escaping pasted text ----

    /// <summary>The text to insert when <paramref name="text"/> is pasted at <paramref name="start"/>: escaped when the caret is inside a quoted string, else unchanged.</summary>
    public string EscapeForPaste(int start, string text)
    {
        if (!options.EscapeInString || !EscapingLanguages.Contains(language) || text.Length == 0) return text;
        var line = d.GetLineByOffset(start);
        int column = start - line.Offset;
        var list = tokens.TokensOfLine(line.LineNumber);
        int index = -1;
        for (int i = 0; i < list.Count; i++)
            if (IsString(list[i]) && list[i].Start <= column && column <= list[i].End) { index = i; break; }
        if (index < 0) return text;
        int first = index;
        while (first > 0 && IsString(list[first - 1]) && list[first - 1].End == list[first].Start) first--;
        char quote = d.GetCharAt(line.Offset + list[first].Start);
        if (quote is not ('"' or '\'')) return text;
        int last = index;
        while (last + 1 < list.Count && IsString(list[last + 1]) && list[last + 1].Start == list[last].End) last++;
        // the caret must be between the quotes: not before the opening one, not after a closing one
        int runStart = list[first].Start, runEnd = list[last].End;
        bool closed = runEnd - runStart >= 2 && d.GetCharAt(line.Offset + runEnd - 1) == quote;
        if (column <= runStart || (closed && column >= runEnd)) return text;

        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (language is "html" or "xml") sb.Append(c == quote ? (quote == '"' ? "&quot;" : "&#39;") : c.ToString());
            else if (c == '\\') sb.Append("\\\\");
            else if (c == quote) sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else sb.Append(c);
        }
        return sb.ToString().Replace("\\r\\n", "\\n", StringComparison.Ordinal);
    }

    // ---- Enter: JSON comma, SQL END ----

    /// <summary>Enter at the end of a JSON value that another value follows: the comma is added. Enter after SQL <c>BEGIN</c>: an <c>END</c> line follows the new one.</summary>
    public bool TryPlanLineBreak(Sel sel, string newLine, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = sel;
        if (!sel.IsEmpty) return false;
        var line = d.GetLineByOffset(sel.Caret);
        if (language == "json" && options.JsonComma && TryJsonComma(line, sel.Caret, newLine, out edit, out result)) return true;
        if (language == "sql" && options.SqlEnd && TrySqlEnd(line, sel.Caret, newLine, out edit, out result)) return true;
        return false;
    }

    private (int TrimmedEnd, Token Last)? LastTokenBefore(DocumentLine line, int caret)
    {
        if (!d.GetText(caret, line.EndOffset - caret).All(char.IsWhiteSpace)) return null;
        int end = caret - line.Offset;
        while (end > 0 && char.IsWhiteSpace(d.GetCharAt(line.Offset + end - 1))) end--;
        return TokenEndingAt(line.LineNumber, end) is { } t ? (end, t) : null;
    }

    private string NextSignificant(int fromLine)
    {
        for (int n = fromLine + 1; n <= d.LineCount; n++)
        {
            var l = d.GetLineByNumber(n);
            string text = d.GetText(l.Offset, l.Length).Trim();
            if (text.Length > 0) return text;
        }
        return "";
    }

    private bool TryJsonComma(DocumentLine line, int caret, string newLine, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = default;
        if (LastTokenBefore(line, caret) is not { } last) return false;
        string text = Text(line, last.Last);
        string kind = last.Last.Kind;
        if (!(kind is "string" or "number" || (kind is "constant" or "keyword" && text is "true" or "false" or "null") || (kind == "punctuation" && text is "}" or "]"))) return false;
        string next = NextSignificant(line.LineNumber);
        if (next.Length == 0 || next[0] is '}' or ']' or ',') return false;
        int indent = 0;
        while (indent < line.Length && d.GetCharAt(line.Offset + indent) is ' ' or '\t') indent++;
        string insert = "," + newLine + d.GetText(line.Offset, indent);
        int at = line.Offset + last.TrimmedEnd;
        edit = new(at, caret - at, insert);
        result = Sel.At(at + insert.Length);
        return true;
    }

    private bool TrySqlEnd(DocumentLine line, int caret, string newLine, out ViewEdit edit, out Sel result)
    {
        edit = default;
        result = default;
        if (LastTokenBefore(line, caret) is not { } last || last.Last.Kind != "keyword") return false;
        string word = Text(line, last.Last);
        if (!word.Equals("begin", StringComparison.OrdinalIgnoreCase)) return false;
        int indent = 0;
        while (indent < line.Length && d.GetCharAt(line.Offset + indent) is ' ' or '\t') indent++;
        if (IsClosed(line, indent)) return false;
        string pad = d.GetText(line.Offset, indent);
        string end = word == word.ToUpperInvariant() ? "END" : "end";
        string head = newLine + pad + indentUnit;
        int at = line.Offset + last.TrimmedEnd;
        edit = new(at, caret - at, head + newLine + pad + end);
        result = Sel.At(at + head.Length);
        return true;
    }

    /// <summary>True when the first later line that is not indented deeper than the BEGIN line starts with END.</summary>
    private bool IsClosed(DocumentLine line, int indent)
    {
        for (int n = line.LineNumber + 1; n <= d.LineCount; n++)
        {
            var l = d.GetLineByNumber(n);
            string text = d.GetText(l.Offset, l.Length);
            string trimmed = text.TrimStart();
            if (trimmed.Length == 0 || text.Length - trimmed.Length > indent) continue;
            return trimmed.StartsWith("end", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // ---- selecting a PHP variable ----

    /// <summary>The name of the PHP variable at <paramref name="offset"/> without its dollar signs; false when no variable is there.</summary>
    public bool TrySelectVariable(int offset, out int start, out int end)
    {
        start = end = 0;
        if (!options.SelectVariable || language != "php") return false;
        var line = d.GetLineByOffset(offset);
        if (TokenCovering(line.LineNumber, offset - line.Offset) is not { Kind: "variable" } t) return false;
        string text = Text(line, t);
        int dollars = 0;
        while (dollars < text.Length && text[dollars] == '$') dollars++;
        if (dollars == text.Length) return false;
        (start, end) = (line.Offset + t.Start + dollars, line.Offset + t.End);
        return true;
    }
}

using System.Globalization;
using System.Text;
using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>A selection as anchor and caret offsets of the text view; <see cref="Anchor"/> equals <see cref="Caret"/> when empty.</summary>
public readonly record struct Sel(int Anchor, int Caret)
{
    public int Start => Math.Min(Anchor, Caret);
    public int End => Math.Max(Anchor, Caret);
    public int Length => End - Start;
    public bool IsEmpty => Anchor == Caret;
    public static Sel At(int offset) => new(offset, offset);
}

/// <summary>How a new indentation level is written.</summary>
public sealed record IndentSettings(bool InsertSpaces = true, int IndentSize = 4, int TabSize = 4)
{
    public string Unit => InsertSpaces ? new string(' ', Math.Max(1, IndentSize)) : "\t";
}

/// <summary>
/// Line commands over a text view document. Each takes the whole list of selections (one for a normal selection, one per row
/// of a column selection) and changes the document in one <see cref="TextDocument.RunUpdate"/> block, so the caller makes one
/// undo step of it. Everything is plain text editing; line endings stay as they are.
/// </summary>
public static class LineOperations
{
    private sealed record Block(int FirstLine, int LastLine)
    {
        public int Count => LastLine - FirstLine + 1;
    }

    /// <summary>Lines touched by the selections, overlapping and touching ones merged. A non-empty selection that ends at the start of a line does not touch that line.</summary>
    private static List<Block> Blocks(TextDocument d, IReadOnlyList<Sel> sels)
    {
        var raw = new List<Block>();
        foreach (var s in sels)
        {
            int first = d.GetLineByOffset(s.Start).LineNumber;
            var endLine = d.GetLineByOffset(s.End);
            int last = !s.IsEmpty && s.End == endLine.Offset && endLine.LineNumber > first ? endLine.LineNumber - 1 : endLine.LineNumber;
            raw.Add(new Block(first, last));
        }
        var merged = new List<Block>();
        foreach (var b in raw.OrderBy(b => b.FirstLine))
        {
            if (merged.Count > 0 && b.FirstLine <= merged[^1].LastLine + 1) merged[^1] = merged[^1] with { LastLine = Math.Max(merged[^1].LastLine, b.LastLine) };
            else merged.Add(b);
        }
        return merged;
    }

    private static (int Start, int End) Span(TextDocument d, Block b) =>
        (d.GetLineByNumber(b.FirstLine).Offset, d.GetLineByNumber(b.LastLine).EndOffset + d.GetLineByNumber(b.LastLine).DelimiterLength);

    private static string NewLineOf(TextDocument d, int lineNumber)
    {
        for (int n = lineNumber; n >= 1; n--)
        {
            var l = d.GetLineByNumber(n);
            if (l.DelimiterLength > 0) return d.GetText(l.EndOffset, l.DelimiterLength);
        }
        for (int n = lineNumber + 1; n <= d.LineCount; n++)
        {
            var l = d.GetLineByNumber(n);
            if (l.DelimiterLength > 0) return d.GetText(l.EndOffset, l.DelimiterLength);
        }
        return Environment.NewLine;
    }

    private static Sel Place(TextDocument d, int line, int column) =>
        Sel.At(LineOffset(d, line, column));

    private static int LineOffset(TextDocument d, int line, int column)
    {
        var l = d.GetLineByNumber(Math.Clamp(line, 1, d.LineCount));
        return l.Offset + Math.Clamp(column, 0, l.Length);
    }

    // ---- delete ----

    public static IReadOnlyList<Sel> DeleteLines(TextDocument d, IReadOnlyList<Sel> sels)
    {
        var blocks = Blocks(d, sels);
        var result = new List<Sel>();
        int removedLines = 0;
        using (d.RunUpdate())
        {
            foreach (var b in blocks.AsEnumerable().Reverse())
            {
                var (start, end) = Span(d, b);
                // the last line has no delimiter: take the one before it, so no empty line is left behind
                if (end == d.TextLength && start > 0 && d.GetLineByNumber(b.FirstLine).PreviousLine is { } prev) start = prev.EndOffset;
                d.Remove(start, end - start);
            }
        }
        foreach (var b in blocks)
        {
            removedLines += b.Count;
            result.Add(Place(d, b.FirstLine - (removedLines - b.Count), 0));
        }
        return Distinct(result);
    }

    // ---- duplicate ----

    public static IReadOnlyList<Sel> DuplicateLines(TextDocument d, IReadOnlyList<Sel> sels)
    {
        var blocks = Blocks(d, sels);
        var mapped = Capture(d, sels);
        using (d.RunUpdate())
        {
            foreach (var b in blocks.AsEnumerable().Reverse())
            {
                var (start, end) = Span(d, b);
                string text = d.GetText(start, end - start);
                if (end == d.TextLength) d.Insert(end, NewLineOf(d, b.LastLine) + text);
                else d.Insert(end, text);
            }
        }
        return Restore(d, mapped, line =>
        {
            int shift = 0;
            foreach (var b in blocks)
            {
                if (line < b.FirstLine) break;
                shift += b.Count;
                if (line <= b.LastLine) break;
            }
            return line + shift;
        });
    }

    // ---- move ----

    /// <summary>Moves the lines of every selection up (-1) or down (+1) past the neighbouring line. Lines that cannot move stay; a block touching the edge blocks only itself.</summary>
    public static IReadOnlyList<Sel> MoveLines(TextDocument d, IReadOnlyList<Sel> sels, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var blocks = Blocks(d, sels);
        var mapped = Capture(d, sels);
        var moved = new List<Block>();
        using (d.RunUpdate())
        {
            foreach (var b in direction < 0 ? blocks : blocks.AsEnumerable().Reverse())
            {
                int neighbour = direction < 0 ? b.FirstLine - 1 : b.LastLine + 1;
                if (neighbour < 1 || neighbour > d.LineCount) continue;
                if (blocks.Any(o => o != b && neighbour >= o.FirstLine && neighbour <= o.LastLine)) continue;
                var lines = direction < 0 ? new Block(neighbour, b.LastLine) : new Block(b.FirstLine, neighbour);
                var (start, end) = Span(d, lines);
                var bodies = new List<string>();
                var delims = new List<string>();
                for (int n = lines.FirstLine; n <= lines.LastLine; n++)
                {
                    var l = d.GetLineByNumber(n);
                    bodies.Add(d.GetText(l.Offset, l.Length));
                    delims.Add(d.GetText(l.EndOffset, l.DelimiterLength));
                }
                // the neighbour jumps over the whole block: rotate the bodies, keep the delimiters in their slots
                if (direction < 0) bodies.Add(PopFirst(bodies)); else bodies.Insert(0, PopLast(bodies));
                var sb = new StringBuilder();
                for (int i = 0; i < bodies.Count; i++) sb.Append(bodies[i]).Append(delims[i]);
                d.Replace(start, end - start, sb.ToString());
                moved.Add(b);
            }
        }
        return Restore(d, mapped, line => moved.Any(b => line >= b.FirstLine && line <= b.LastLine) ? line + direction : line);
    }

    private static string PopFirst(List<string> l) { var v = l[0]; l.RemoveAt(0); return v; }

    private static string PopLast(List<string> l) { var v = l[^1]; l.RemoveAt(l.Count - 1); return v; }

    // ---- join ----

    /// <summary>Joins each selected line with the next one (several lines of a selection into one). Leading blanks of the joined line are replaced by one space.</summary>
    public static IReadOnlyList<Sel> JoinLines(TextDocument d, IReadOnlyList<Sel> sels)
    {
        var joins = new List<(int First, int Last)>();
        foreach (var s in sels)
        {
            int first = d.GetLineByOffset(s.Start).LineNumber;
            int last = d.GetLineByOffset(s.End).LineNumber;
            if (last == first) last++;
            if (!s.IsEmpty && last > first && s.End == d.GetLineByNumber(last).Offset && last - first > 1) last--;
            joins.Add((first, Math.Min(last, d.LineCount)));
        }
        var merged = new List<(int First, int Last)>();
        foreach (var j in joins.OrderBy(j => j.First))
        {
            if (merged.Count > 0 && j.First <= merged[^1].Last) merged[^1] = (merged[^1].First, Math.Max(merged[^1].Last, j.Last));
            else merged.Add(j);
        }
        var result = new List<Sel>();
        var carets = new List<(int Line, int Column)>();
        using (d.RunUpdate())
        {
            foreach (var (first, last) in merged.AsEnumerable().Reverse())
            {
                if (last <= first) continue;
                int caretColumn = 0;
                for (int n = last; n > first; n--)
                {
                    var upper = d.GetLineByNumber(n - 1);
                    var lower = d.GetLineByNumber(n);
                    int blanks = 0;
                    while (blanks < lower.Length && d.GetCharAt(lower.Offset + blanks) is ' ' or '\t') blanks++;
                    bool glue = upper.Length > 0 && d.GetCharAt(upper.EndOffset - 1) is not (' ' or '\t') && blanks < lower.Length;
                    caretColumn = upper.Length;
                    d.Replace(upper.EndOffset, upper.DelimiterLength + blanks, glue ? " " : "");
                }
                carets.Add((first, caretColumn));
            }
        }
        foreach (var (line, column) in carets.AsEnumerable().Reverse()) result.Add(Place(d, line, column));
        return result.Count == 0 ? sels : Distinct(result);
    }

    // ---- indent ----

    /// <summary>Indents every touched non-blank line by one level.</summary>
    public static IReadOnlyList<Sel> Indent(TextDocument d, IReadOnlyList<Sel> sels, IndentSettings settings)
    {
        var blocks = Blocks(d, sels);
        var mapped = Capture(d, sels);
        string unit = settings.Unit;
        var shifts = new Dictionary<int, int>();
        using (d.RunUpdate())
        {
            foreach (var b in blocks.AsEnumerable().Reverse())
                for (int n = b.LastLine; n >= b.FirstLine; n--)
                {
                    var l = d.GetLineByNumber(n);
                    if (l.Length == 0 || IsBlank(d, l)) continue;
                    d.Insert(l.Offset, unit);
                    shifts[n] = unit.Length;
                }
        }
        return Restore(d, mapped, line => line, (line, col) => col + (shifts.TryGetValue(line, out var s) ? s : 0));
    }

    /// <summary>Removes one indentation level (a tab or up to one indent size of spaces) from every touched line.</summary>
    public static IReadOnlyList<Sel> Outdent(TextDocument d, IReadOnlyList<Sel> sels, IndentSettings settings)
    {
        var blocks = Blocks(d, sels);
        var mapped = Capture(d, sels);
        var removedPerLine = new Dictionary<int, int>();
        using (d.RunUpdate())
        {
            foreach (var b in blocks.AsEnumerable().Reverse())
                for (int n = b.LastLine; n >= b.FirstLine; n--)
                {
                    var l = d.GetLineByNumber(n);
                    int remove = 0;
                    if (l.Length > 0 && d.GetCharAt(l.Offset) == '\t') remove = 1;
                    else while (remove < Math.Max(1, settings.IndentSize) && remove < l.Length && d.GetCharAt(l.Offset + remove) == ' ') remove++;
                    if (remove == 0) continue;
                    d.Remove(l.Offset, remove);
                    removedPerLine[n] = remove;
                }
        }
        return Restore(d, mapped, line => line, (line, col) => removedPerLine.TryGetValue(line, out var r) ? Math.Max(0, col - r) : col);
    }

    /// <summary>The Tab key without a command that handles it: inserts spaces up to the next indent stop, or a tab, replacing the selection.</summary>
    public static IReadOnlyList<Sel> InsertIndent(TextDocument d, IReadOnlyList<Sel> sels, IndentSettings settings)
    {
        var result = new List<Sel>();
        int shift = 0;
        using (d.RunUpdate())
        {
            foreach (var s in sels.OrderBy(s => s.Start))
            {
                int start = s.Start + shift;
                string text = "\t";
                if (settings.InsertSpaces)
                {
                    int column = VisualColumn(d, start, settings.TabSize) ;
                    int size = Math.Max(1, settings.IndentSize);
                    text = new string(' ', size - column % size);
                }
                d.Replace(start, s.Length, text);
                shift += text.Length - s.Length;
                result.Add(Sel.At(start + text.Length));
            }
        }
        return result;
    }

    private static int VisualColumn(TextDocument d, int offset, int tabSize)
    {
        var line = d.GetLineByOffset(offset);
        int col = 0;
        for (int i = line.Offset; i < offset; i++)
            col = d.GetCharAt(i) == '\t' ? col + tabSize - col % tabSize : col + 1;
        return col;
    }

    private static bool IsBlank(TextDocument d, DocumentLine l)
    {
        for (int i = l.Offset; i < l.EndOffset; i++)
            if (d.GetCharAt(i) is not (' ' or '\t')) return false;
        return true;
    }

    // ---- case ----

    /// <summary>Upper or lower case for every non-empty selection; empty selections are left alone. Uses the invariant culture so the result does not depend on the machine.</summary>
    public static IReadOnlyList<Sel> ChangeCase(TextDocument d, IReadOnlyList<Sel> sels, bool upper)
    {
        var result = new List<Sel>();
        int shift = 0;
        using (d.RunUpdate())
        {
            foreach (var s in sels.OrderBy(s => s.Start))
            {
                if (s.IsEmpty) { result.Add(new Sel(s.Anchor + shift, s.Caret + shift)); continue; }
                string text = d.GetText(s.Start, s.Length);
                string changed = upper ? text.ToUpper(CultureInfo.InvariantCulture) : text.ToLower(CultureInfo.InvariantCulture);
                int start = s.Start + shift;
                if (changed != text) d.Replace(start, s.Length, changed);
                shift += changed.Length - s.Length;
                result.Add(s.Anchor <= s.Caret ? new Sel(start, start + changed.Length) : new Sel(start + changed.Length, start));
            }
        }
        return result;
    }

    // ---- selection bookkeeping ----

    private sealed record Endpoint(int Line, int Column);

    private static List<(Endpoint Anchor, Endpoint Caret)> Capture(TextDocument d, IReadOnlyList<Sel> sels)
    {
        Endpoint At(int offset)
        {
            var l = d.GetLineByOffset(offset);
            return new Endpoint(l.LineNumber, offset - l.Offset);
        }
        return [.. sels.Select(s => (At(s.Anchor), At(s.Caret)))];
    }

    private static IReadOnlyList<Sel> Restore(TextDocument d, List<(Endpoint Anchor, Endpoint Caret)> captured, Func<int, int> mapLine, Func<int, int, int>? mapColumn = null)
    {
        int At(Endpoint e) => LineOffset(d, mapLine(e.Line), mapColumn?.Invoke(e.Line, e.Column) ?? e.Column);
        return [.. captured.Select(c => new Sel(At(c.Anchor), At(c.Caret)))];
    }

    private static IReadOnlyList<Sel> Distinct(List<Sel> sels) => [.. sels.Distinct()];
}

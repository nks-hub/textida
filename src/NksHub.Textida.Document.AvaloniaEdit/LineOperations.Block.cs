using System.Globalization;
using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>What <see cref="LineOperations.InsertIntoLines"/> writes.</summary>
public enum LineInsertMode { Text, Numbering, Bullets }

/// <summary>Where it is written: at the start of the lines, at their end, before a column, or at each selection instead of at lines.</summary>
public enum LineInsertPlace { LineStart, LineEnd, Column, Selections }

/// <summary>
/// Options of "Insert text into lines". <see cref="Text"/> is the text, or the bullet in <see cref="LineInsertMode.Bullets"/>. Numbering writes
/// <see cref="Start"/>, <see cref="Start"/> + 1, … padded with zeros to <see cref="Width"/> digits; <see cref="Separator"/> goes between the number
/// and the text of the line (after the number at the start of a line, before it at the end). <see cref="Column"/> is 1-based.
/// </summary>
public sealed record LineInsertOptions(
    LineInsertMode Mode = LineInsertMode.Text,
    string Text = "",
    LineInsertPlace Place = LineInsertPlace.LineStart,
    int Column = 1,
    bool SkipIndent = false,
    bool SkipEmptyLines = false,
    long Start = 1,
    int Width = 1,
    string Separator = "");

public static partial class LineOperations
{
    // ---- replace selections ----

    /// <summary>
    /// Replaces every selection with the text <paramref name="textFor"/> gives for its index (in document order). The caret goes after the new
    /// text, or before it with <paramref name="caretBefore"/>; with <paramref name="selectNew"/> the new text stays selected instead.
    /// </summary>
    public static IReadOnlyList<Sel> ReplaceSelections(TextDocument d, IReadOnlyList<Sel> sels, Func<int, string> textFor, bool caretBefore = false, bool selectNew = false)
    {
        var result = new List<Sel>();
        int shift = 0;
        using (d.RunUpdate())
        {
            int index = 0;
            foreach (var s in sels.OrderBy(s => s.Start))
            {
                string text = textFor(index++);
                int start = s.Start + shift;
                d.Replace(start, s.Length, text);
                shift += text.Length - s.Length;
                result.Add(selectNew ? new Sel(start, start + text.Length) : Sel.At(caretBefore ? start : start + text.Length));
            }
        }
        return result;
    }

    /// <summary>
    /// Replaces every selection with the text and the caret offsets (inside that text) <paramref name="pieceFor"/> gives for its index (in document order).
    /// Every caret of every piece stays as a selection; a piece without carets leaves one after its text.
    /// </summary>
    public static IReadOnlyList<Sel> ReplaceSelectionsWithCarets(TextDocument d, IReadOnlyList<Sel> sels, Func<int, (string Text, IReadOnlyList<int> Carets)> pieceFor)
    {
        var result = new List<Sel>();
        int shift = 0;
        using (d.RunUpdate())
        {
            int index = 0;
            foreach (var s in sels.OrderBy(s => s.Start))
            {
                var (text, carets) = pieceFor(index++);
                int start = s.Start + shift;
                d.Replace(start, s.Length, text);
                shift += text.Length - s.Length;
                if (carets.Count == 0) result.Add(Sel.At(start + text.Length));
                else foreach (int c in carets) result.Add(Sel.At(start + c));
            }
        }
        return result;
    }

    // ---- insert into lines ----

    /// <summary>The lines the command works on: those of the selections, or the whole document for a single caret without a selection.</summary>
    private static List<Block> TargetBlocks(TextDocument d, IReadOnlyList<Sel> sels) =>
        sels.Count == 1 && sels[0].IsEmpty ? [new Block(1, d.LineCount)] : Blocks(d, sels);

    public static string FormatNumber(long value, int width)
    {
        string digits = Math.Abs((decimal)value).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, width), '0');
        return value < 0 ? "-" + digits : digits;
    }

    /// <summary>The text that goes into line number <paramref name="ordinal"/> (0-based among the lines written to).</summary>
    private static string InsertedText(LineInsertOptions o, int ordinal, bool atEnd) => o.Mode switch
    {
        LineInsertMode.Numbering => atEnd ? o.Separator + FormatNumber(o.Start + ordinal, o.Width) : FormatNumber(o.Start + ordinal, o.Width) + o.Separator,
        _ => o.Text,
    };

    /// <summary>Inserts text, numbers or bullets into the lines of the selections (the whole document when there is one caret and no selection), or at each selection.</summary>
    public static IReadOnlyList<Sel> InsertIntoLines(TextDocument d, IReadOnlyList<Sel> sels, LineInsertOptions o)
    {
        var inserts = new List<(int Offset, string Text)>();
        if (o.Place == LineInsertPlace.Selections)
        {
            foreach (var s in sels.OrderBy(s => s.Start)) inserts.Add((s.Start, InsertedText(o with { Separator = "" }, inserts.Count, atEnd: false)));
        }
        else
        {
            foreach (var b in TargetBlocks(d, sels))
                for (int n = b.FirstLine; n <= b.LastLine; n++)
                {
                    var line = d.GetLineByNumber(n);
                    if (o.SkipEmptyLines && IsBlank(d, line)) continue;
                    int at = o.Place switch
                    {
                        LineInsertPlace.LineEnd => line.EndOffset,
                        LineInsertPlace.Column => line.Offset + Math.Clamp(o.Column - 1, 0, line.Length),
                        _ => line.Offset + (o.SkipIndent ? LeadingBlanks(d, line) : 0),
                    };
                    inserts.Add((at, InsertedText(o, inserts.Count, o.Place == LineInsertPlace.LineEnd)));
                }
        }
        return ApplyInserts(d, sels, inserts);
    }

    private static int LeadingBlanks(TextDocument d, DocumentLine line)
    {
        int n = 0;
        while (n < line.Length && d.GetCharAt(line.Offset + n) is ' ' or '\t') n++;
        return n;
    }

    /// <summary>Applies insertions given in ascending order of offset as one update; the selections move with the text, ending after text inserted at their edge.</summary>
    private static IReadOnlyList<Sel> ApplyInserts(TextDocument d, IReadOnlyList<Sel> sels, List<(int Offset, string Text)> inserts)
    {
        int Moved(int offset) => offset + inserts.TakeWhile(i => i.Offset <= offset).Sum(i => i.Text.Length);
        var result = new List<Sel>();
        foreach (var s in sels) result.Add(new Sel(Moved(s.Anchor), Moved(s.Caret)));
        using (d.RunUpdate())
        {
            foreach (var (offset, text) in Enumerable.Reverse(inserts))
                if (text.Length > 0) d.Insert(offset, text);
        }
        return result;
    }

    // ---- sort ----

    /// <summary>Sorts the lines of every selection block (the whole document for one caret) by <paramref name="options"/>; the new block is selected, or the caret stays for a lone caret.</summary>
    public static IReadOnlyList<Sel> SortLines(TextDocument d, IReadOnlyList<Sel> sels, SortOptions options) =>
        ReplaceBlocks(d, sels, TargetBlocks(d, sels), lines => LineText.Sort(lines, options));

    /// <summary>The text the first <paramref name="maxLines"/> lines of the sorted first block would have, for a preview before the sort runs.</summary>
    public static string SortPreview(TextDocument d, IReadOnlyList<Sel> sels, SortOptions options, int maxLines)
    {
        var block = TargetBlocks(d, sels)[0];
        var sorted = LineText.Sort(BlockLines(d, block), options);
        return string.Join(NewLineOf(d, block.LastLine), sorted.Take(maxLines));
    }

    // ---- align and wrap ----

    /// <summary>Aligns the lines of the selections (the whole document for one caret) to the width of the longest line.</summary>
    public static IReadOnlyList<Sel> JustifyLines(TextDocument d, IReadOnlyList<Sel> sels, Justification mode) =>
        ReplaceBlocks(d, sels, TargetBlocks(d, sels), lines => LineText.Justify(lines, mode));

    /// <summary>Wraps the paragraphs of the selections, or the paragraph of a caret without selection, at <paramref name="width"/> columns.</summary>
    public static IReadOnlyList<Sel> ReformatParagraphs(TextDocument d, IReadOnlyList<Sel> sels, int width)
    {
        var blocks = new List<Block>();
        foreach (var s in sels)
        {
            if (!s.IsEmpty) { blocks.AddRange(Blocks(d, [s])); continue; }
            int line = d.GetLineByOffset(s.Start).LineNumber;
            if (IsBlank(d, d.GetLineByNumber(line))) continue;
            int first = line, last = line;
            while (first > 1 && !IsBlank(d, d.GetLineByNumber(first - 1))) first--;
            while (last < d.LineCount && !IsBlank(d, d.GetLineByNumber(last + 1))) last++;
            blocks.Add(new Block(first, last));
        }
        return ReplaceBlocks(d, sels, MergeBlocks(blocks), lines => LineText.Reformat(lines, width), caretToEnd: true);
    }

    private static List<Block> MergeBlocks(List<Block> blocks)
    {
        var merged = new List<Block>();
        foreach (var b in blocks.OrderBy(b => b.FirstLine))
        {
            if (merged.Count > 0 && b.FirstLine <= merged[^1].LastLine + 1) merged[^1] = merged[^1] with { LastLine = Math.Max(merged[^1].LastLine, b.LastLine) };
            else merged.Add(b);
        }
        return merged;
    }

    private static List<string> BlockLines(TextDocument d, Block b)
    {
        var lines = new List<string>(b.Count);
        for (int n = b.FirstLine; n <= b.LastLine; n++)
        {
            var l = d.GetLineByNumber(n);
            lines.Add(d.GetText(l.Offset, l.Length));
        }
        return lines;
    }

    /// <summary>
    /// Replaces the text of each block (its lines without the line ending after the last one) by the lines <paramref name="change"/> returns, joined
    /// with the line ending of the document. Afterwards each block is selected; a single caret stays on its line and column instead
    /// (or goes to the end of the block with <paramref name="caretToEnd"/>).
    /// </summary>
    private static IReadOnlyList<Sel> ReplaceBlocks(TextDocument d, IReadOnlyList<Sel> sels, List<Block> blocks, Func<IReadOnlyList<string>, IReadOnlyList<string>> change, bool caretToEnd = false)
    {
        bool lone = sels.Count == 1 && sels[0].IsEmpty;
        var mapped = Capture(d, sels);
        var spans = new List<(int Start, int OldLength, string Text)>();
        foreach (var b in blocks)
        {
            string text = string.Join(NewLineOf(d, b.LastLine), change(BlockLines(d, b)));
            int start = d.GetLineByNumber(b.FirstLine).Offset;
            spans.Add((start, d.GetLineByNumber(b.LastLine).EndOffset - start, text));
        }
        using (d.RunUpdate())
        {
            foreach (var (start, oldLength, text) in Enumerable.Reverse(spans))
                if (d.GetText(start, oldLength) != text) d.Replace(start, oldLength, text);
        }
        if (lone) return caretToEnd && spans.Count > 0 ? [Sel.At(spans[0].Start + spans[0].Text.Length)] : Restore(d, mapped, line => line);
        var result = new List<Sel>();
        int shift = 0;
        foreach (var (start, oldLength, text) in spans)
        {
            result.Add(new Sel(start + shift, start + shift + text.Length));
            shift += text.Length - oldLength;
        }
        return result;
    }

    // ---- select lines by range ----

    /// <summary>One selection per range of line numbers (1-based, clamped to the document), each from the start of its first line to the start of the line after its last, so a copy brings whole lines.</summary>
    public static IReadOnlyList<Sel> SelectLineRanges(TextDocument d, IEnumerable<(int From, int To)> ranges)
    {
        var result = new List<Sel>();
        foreach (var (from, to) in ranges)
        {
            if (from > d.LineCount) continue;
            var last = d.GetLineByNumber(Math.Min(to, d.LineCount));
            result.Add(new Sel(d.GetLineByNumber(from).Offset, last.EndOffset + last.DelimiterLength));
        }
        return result;
    }
}

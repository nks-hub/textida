using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Paste into a column (block) selection: one line of the clipboard per row.</summary>
public static class ColumnEdit
{
    /// <summary>
    /// <paramref name="rows"/> are the per-row ranges of a block selection (consecutive lines, the same character column).
    /// A one-line text goes into every row, replacing the row's selected part. A text of several lines puts line i into row i;
    /// rows below the selection are filled too (short rows are padded with spaces, missing lines are added). Returns the caret after the last inserted text.
    /// </summary>
    public static IReadOnlyList<Sel> Paste(TextDocument d, IReadOnlyList<Sel> rows, string text, string newLine)
    {
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();
        if (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var first = d.GetLineByOffset(rows[0].Start);
        int column = rows[0].Start - first.Offset;
        int count = lines.Count == 1 ? rows.Count : Math.Max(rows.Count, lines.Count);
        int lastLine = first.LineNumber + count - 1;

        int caret = 0, delta = 0;
        using (d.RunUpdate())
        {
            while (d.LineCount < lastLine) d.Insert(d.TextLength, newLine);
            for (int i = count - 1; i >= 0; i--)
            {
                string piece = lines.Count == 1 ? lines[0] : i < lines.Count ? lines[i] : "";
                int at;
                int remove = 0;
                var line = d.GetLineByNumber(first.LineNumber + i);
                if (i < rows.Count) { at = rows[i].Start; remove = rows[i].Length; }
                else
                {
                    int pad = Math.Max(0, column - line.Length);
                    piece = new string(' ', pad) + piece;
                    at = line.Offset + Math.Min(column, line.Length);
                }
                d.Replace(at, remove, piece);
                if (i == count - 1) caret = at + piece.Length;
                else delta += piece.Length - remove;
            }
        }
        return [Sel.At(caret + delta)];
    }
}

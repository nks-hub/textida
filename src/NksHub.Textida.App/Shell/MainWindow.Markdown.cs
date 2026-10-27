using AvaloniaEdit.Document;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>The Markdown commands that change the text (P9.3.3): aligning, repairing and walking through the table under the caret and writing the table of contents between its markers.</summary>
internal sealed partial class MainWindow
{
    private static string[] TextLines(TextDocument d) => [.. d.Lines.Select(l => d.GetText(l.Offset, l.Length))];

    private static IReadOnlyList<Sel> Clamped(TextDocument d, IReadOnlyList<Sel> sels) =>
        [.. sels.Select(s => new Sel(Math.Min(s.Anchor, d.TextLength), Math.Min(s.Caret, d.TextLength)))];

    /// <summary>The lines of a document read on demand, so asking about the caret line does not copy a large file.</summary>
    private sealed class DocumentLines(TextDocument document) : IReadOnlyList<string>
    {
        public int Count => document.LineCount;

        public string this[int index] { get { var l = document.GetLineByNumber(index + 1); return document.GetText(l.Offset, l.Length); } }

        public IEnumerator<string> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The editor, its lines and the caret place when the caret is one plain caret (or a selection in one line) of a Markdown document.</summary>
    private (EditorSurface Surface, IReadOnlyList<string> Lines, int Line, int Column)? MarkdownCaret()
    {
        if (Active is not { } tab || tab.Doc.Language.Id != "markdown" || ActiveEditor is not { IsReadOnly: false, IsMulti: false, IsBlockSelection: false } surface) return null;
        var d = surface.Document;
        var sel = surface.GetSelections()[0];
        var line = d.GetLineByOffset(sel.Start);
        if (sel.End > line.EndOffset) return null;
        return (surface, new DocumentLines(d), line.LineNumber - 1, sel.Start - line.Offset);
    }

    /// <summary>True when the caret is in a Markdown table with a header and a row of dashes; the <c>inMarkdownTable</c> context.</summary>
    internal bool InMarkdownTable => MarkdownCaret() is { } c && MarkdownTools.TableAround(c.Lines, c.Line) is not null;

    /// <summary>Puts the rewritten table lines in place, as one undo step, and selects the cell the edit points at.</summary>
    private CommandOutcome ApplyTableEdit(MarkdownTools.TableEdit edit) => RunEdit((s, _) =>
    {
        var d = s.Document;
        int last = edit.First + edit.Count - 1;
        if (edit.Lines.Count == edit.Count)
        {
            for (int i = last; i >= edit.First; i--)
            {
                var line = d.GetLineByNumber(i + 1);
                if (d.GetText(line.Offset, line.Length) != edit.Lines[i - edit.First]) d.Replace(line.Offset, line.Length, edit.Lines[i - edit.First]);
            }
        }
        else
        {
            var head = d.GetLineByNumber(edit.First + 1);
            string eol = head.DelimiterLength > 0 ? d.GetText(head.EndOffset, head.DelimiterLength) : s.Doc.NewLine(s.Settings.ConfiguredEol);
            d.Replace(head.Offset, d.GetLineByNumber(last + 1).EndOffset - head.Offset, string.Join(eol, edit.Lines));
        }
        int at = d.GetLineByNumber(edit.First + edit.CaretRow + 1).Offset + edit.CaretStart;
        return [new Sel(at, at + edit.CaretLength)];
    });

    /// <summary>Tab or Shift+Tab in a table: realigns it and selects the next or previous cell.</summary>
    internal CommandOutcome MoveTableCell(bool forward) =>
        MarkdownCaret() is { } c && MarkdownTools.MoveCell(c.Lines, c.Line, c.Column, forward) is { } edit ? ApplyTableEdit(edit) : CommandOutcome.NotHandled;

    /// <summary>Shift+Enter in a table: an empty row below the caret row.</summary>
    internal CommandOutcome AddTableRow() =>
        MarkdownCaret() is { } c && MarkdownTools.NewRow(c.Lines, c.Line) is { } edit ? ApplyTableEdit(edit) : CommandOutcome.NotHandled;

    /// <summary>Repairs the table block under the caret (missing separator row, short rows) and aligns it.</summary>
    internal CommandOutcome RepairMarkdownTable()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        if (MarkdownCaret() is { } c && MarkdownTools.Repair(c.Lines, c.Line) is { } edit) return ApplyTableEdit(edit);
        SetStatusMessage(Loc.Get(K.MarkdownNoTableBlock));
        return CommandOutcome.Handled;
    }

    /// <summary>Rewrites the table under the caret with aligned columns, as one undo step; the line ends stay as they are.</summary>
    internal CommandOutcome FormatMarkdownTable()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var lines = TextLines(surface.Document);
        int caretLine = surface.Document.GetLineByOffset(surface.CaretOffset).LineNumber - 1;
        if (MarkdownTools.TableAround(lines, caretLine) is not { } table)
        {
            SetStatusMessage(Loc.Get(K.MarkdownNoTable));
            return CommandOutcome.Handled;
        }
        (int first, int last) = table;
        var formatted = MarkdownTools.FormatTable(lines[first..(last + 1)]);
        return RunEdit((s, sels) =>
        {
            var d = s.Document;
            for (int i = last; i >= first; i--)
            {
                var line = d.GetLineByNumber(i + 1);
                if (d.GetText(line.Offset, line.Length) != formatted[i - first]) d.Replace(line.Offset, line.Length, formatted[i - first]);
            }
            return Clamped(d, sels);
        });
    }

    /// <summary>Writes the links to the headings between <c>&lt;!-- TOC --&gt;</c> and <c>&lt;!-- /TOC --&gt;</c> and touches nothing else; without the markers they are added at the caret.</summary>
    internal CommandOutcome UpdateMarkdownToc()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var lines = TextLines(surface.Document);
        var toc = MarkdownTools.TocLines(MarkdownTools.Headings(lines));
        return RunEdit((s, sels) =>
        {
            var d = s.Document;
            if (MarkdownTools.TocBlock(lines) is { } block)
            {
                (int open, int close) = block;
                var openLine = d.GetLineByNumber(open + 1);
                string eol = openLine.DelimiterLength > 0 ? d.GetText(openLine.EndOffset, openLine.DelimiterLength) : "\n";
                int from = openLine.EndOffset, to = d.GetLineByNumber(close + 1).Offset;
                string body = eol + string.Concat(toc.Select(l => l + eol));
                if (d.GetText(from, to - from) != body) d.Replace(from, to - from, body);
            }
            else
            {
                var line = d.GetLineByOffset(s.CaretOffset);
                string eol = line.DelimiterLength > 0 ? d.GetText(line.EndOffset, line.DelimiterLength) : "\n";
                d.Insert(line.Offset, MarkdownTools.TocOpen + eol + string.Concat(toc.Select(l => l + eol)) + MarkdownTools.TocClose + eol);
            }
            return Clamped(d, sels);
        });
    }
}

using AvaloniaEdit.Document;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>The Markdown commands that change the text (P9.3.3): aligning the table under the caret and writing the table of contents between its markers.</summary>
internal sealed partial class MainWindow
{
    private static string[] TextLines(TextDocument d) => [.. d.Lines.Select(l => d.GetText(l.Offset, l.Length))];

    private static IReadOnlyList<Sel> Clamped(TextDocument d, IReadOnlyList<Sel> sels) =>
        [.. sels.Select(s => new Sel(Math.Min(s.Anchor, d.TextLength), Math.Min(s.Caret, d.TextLength)))];

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

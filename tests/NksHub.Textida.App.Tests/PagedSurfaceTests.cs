using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>The paged surface for screen readers (text, selection and caret over block boundaries), for input methods (inline composition) and while its line index is built.</summary>
[Collection("LocState")]
public class PagedSurfaceTests : IDisposable
{
    private const int Mib = 1 << 20;

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedSurfaceTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static PagedDocument PagedOf(DocTab tab) => ((PagedBackend)tab.Doc.Doc.Backend).Paged;

    private static void Pump()
    {
        Thread.Sleep(25);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitFor(Func<bool> done, string what)
    {
        for (int i = 0; i < 600 && !done(); i++) Pump();
        Assert.True(done(), what);
    }

    private DocTab Open(string path)
    {
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        _w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private DocTab OpenIndexed(string path)
    {
        var tab = Open(path);
        WaitFor(() => tab.Paged!.Index.Complete, "index complete");
        return tab;
    }

    private static string[] SplitKeepingTerminators(string text)
    {
        var lines = new List<string>();
        int from = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] is not ('\r' or '\n')) continue;
            lines.Add(text[from..(i + 1)]);
            from = i + 1;
        }
        lines.Add(text[from..]);
        return [.. lines];
    }

    [AvaloniaTheory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void Wide_encodings_split_lines_by_code_unit_not_by_byte(string name)
    {
        // č is 0D 01 / 01 0D, U+0A0D and U+0D0A hold both terminator bytes in either order
        string text = string.Concat(Enumerable.Range(0, 20_000).Select(i => $"čč řádek {i}\r\n\u0A0D x{i}\n\u0D0A y{i}\r\u010D\u010A z{i}\n"));
        var enc = Encoding.GetEncoding(name);
        var tab = OpenIndexed(_temp.Bytes("wide.txt", [.. enc.GetPreamble(), .. enc.GetBytes(text)]));
        var paged = tab.Paged!;
        var lines = SplitKeepingTerminators(text);
        Assert.Equal(lines.Length, paged.Index.Lines);

        using var surface = new PagedTextView.TextSurface(paged);
        int count = surface.LineCount;
        Assert.True(count >= 12);
        Assert.Equal(string.Concat(lines.Take(count)), surface.GetText(0, surface.Length));
    }

    // ---- automation text over block boundaries ----

    [AvaloniaFact]
    public void Automation_text_selection_and_caret_match_the_file_over_loader_and_index_blocks()
    {
        string text = string.Concat(Enumerable.Range(1, 260_000).Select(i => i % 7 == 0 ? $"řádek {i} 日本\r\n" : $"řádek {i} é\n"));
        var tab = OpenIndexed(_temp.Bytes("blocks.txt", Encoding.UTF8.GetBytes(text)));
        var paged = tab.Paged!;
        var lines = SplitKeepingTerminators(text);
        var charStart = new long[lines.Length + 1];
        var byteStart = new long[lines.Length + 1];
        for (int i = 0; i < lines.Length; i++)
        {
            charStart[i + 1] = charStart[i] + lines[i].Length;
            byteStart[i + 1] = byteStart[i] + Encoding.UTF8.GetByteCount(lines[i]);
        }
        Assert.True(byteStart[^1] > 3 * Mib); // several index blocks and many loader windows

        using var surface = new PagedTextView.TextSurface(paged);
        var changes = new List<TextChange>();
        surface.TextChanged += changes.Add;
        int selections = 0;
        surface.SelectionChanged += () => selections++;

        void AssertWindowIsTheFile()
        {
            long first = surface.FirstLine;
            int count = surface.LineCount;
            Assert.True(count > 100);
            Assert.Equal(text.Substring((int)charStart[first], (int)(charStart[first + count] - charStart[first])), surface.GetText(0, surface.Length));
            for (int i = 0; i <= count; i++) Assert.Equal(charStart[first + i] - charStart[first], surface.LineStart(i));
        }

        // three places: one inside a loader window, one at a 128 KiB boundary, one past the first index block
        foreach (long line in new[] { 50_000L, 128 * 1024 / 14 + 1, 120_000L, 230_000L })
        {
            paged.GoToLine(line + 1);
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AssertWindowIsTheFile();
            Assert.InRange(line, surface.FirstLine, surface.FirstLine + surface.LineCount - 1);
            Assert.Equal(charStart[line] - charStart[surface.FirstLine], surface.CaretOffset);
            Assert.True(surface.Selection.IsEmpty);
        }
        Assert.NotEmpty(changes); // the window moved with the view
        Assert.True(changes.Any(c => c.Removed > 0 && c.Inserted > 0));

        // a selection over many lines, with a CRLF and multi-byte characters inside
        long a = 229_990, b = 230_010;
        long selStart = byteStart[a] + Encoding.UTF8.GetByteCount(lines[a][..3]), selEnd = byteStart[b] + Encoding.UTF8.GetByteCount(lines[b][..5]);
        int before = selections;
        paged.SelectBytes(selStart, selEnd);
        long first0 = surface.FirstLine;
        Assert.Equal(charStart[a] + 3 - charStart[first0], surface.Selection.Start);
        Assert.Equal(charStart[b] + 5 - charStart[a] - 3, surface.Selection.Length);
        Assert.Equal(surface.Selection.End, surface.CaretOffset);
        Assert.True(selections > before);
        Assert.Equal(text.Substring((int)charStart[a] + 3, (int)(charStart[b] + 5 - charStart[a] - 3)), TextRange.SelectionOf(surface).GetText());

        // a backwards selection keeps its caret at the start
        paged.SelectBytes(selEnd, selStart);
        Assert.Equal(surface.Selection.Start, surface.CaretOffset);

        // the screen reader sets a selection: it lands on the same bytes
        long f = surface.FirstLine;
        long Bytes(long first, int chars) => byteStart[first] + Encoding.UTF8.GetByteCount(text.Substring((int)charStart[first], chars));
        surface.SetSelection(40, 120);
        Assert.Equal(Bytes(f, 40), paged.SelectionRange.Start);
        Assert.Equal(Bytes(f, 160), paged.SelectionRange.End);
        f = surface.FirstLine;
        surface.SetSelection(10, 0);
        Assert.False(paged.HasSelection);
        Assert.Equal(Bytes(f, 10), paged.CaretOffset);
        Assert.Equal(charStart[f] + 10 - charStart[surface.FirstLine], surface.CaretOffset); // the window may have moved with the view

        // units through the shared range logic: the line of the caret is the file line with its terminator
        paged.GoToLine(231);
        var range = TextRange.Caret(surface);
        range.ExpandToEnclosingUnit(TextUnit.Line);
        Assert.Equal(lines[230], range.GetText());

        // typing is reported as a change of the window text at the caret
        changes.Clear();
        TestApp.Type(_w, "x");
        var typed = Assert.Single(changes);
        Assert.Equal(1, typed.Inserted);
        Assert.Equal(0, typed.Removed);
        Assert.Equal(surface.CaretOffset - 1, typed.Start);
        Assert.Equal(paged.CaretOffset, byteStart[230] + 1);
        Assert.Equal('x', surface.GetText(surface.CaretOffset - 1, 1)[0]);
        Assert.Equal(tab.Doc.Doc.Revision, surface.Revision);
    }

    [AvaloniaFact]
    public void Automation_bounds_and_hit_testing_of_the_paged_surface_agree()
    {
        var tab = OpenIndexed(_temp.Bytes("geometry.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")))));
        _w.UpdateLayout();
        using var surface = new PagedTextView.TextSurface(tab.Paged!);
        var visible = surface.VisibleRange;
        Assert.Equal(0, visible.Start);
        Assert.True(visible.Length > 0);
        Assert.False(surface.IsReadOnly);
        var rects = surface.GetRangeBounds(5, 1); // the "1" of the first line
        var rect = Assert.Single(rects);
        Assert.True(rect.Width > 0 && rect.Height > 0);
        int hit = surface.OffsetFromPoint(rect.X + 1, rect.Y + rect.Height / 2);
        Assert.InRange(hit, 5, 6);
        var lines = surface.GetRangeBounds(0, 14); // two lines
        Assert.Equal(2, lines.Count);
        Assert.True(lines[1].Y > lines[0].Y);
        Assert.Equal(-1, surface.OffsetFromPoint(-5000, -5000));
    }

    // ---- input method ----

    [AvaloniaFact]
    public void Composition_is_drawn_at_the_caret_without_touching_the_document_and_the_commit_lands_once()
    {
        var rows = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")));
        var tab = OpenIndexed(_temp.Bytes("ime.txt", rows));
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        tab.Paged!.RaiseEvent(args);
        var client = Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);
        Assert.True(client.SupportsPreedit);
        var doc = tab.Doc.Doc;
        long revision = doc.Revision;
        var start = client.CursorRectangle;
        int moved = 0;
        client.CursorRectangleChanged += (_, _) => moved++;

        client.SetPreeditText("にほ");
        var narrow = client.CursorRectangle;
        client.SetPreeditText("にほんご");
        var wide = client.CursorRectangle;
        Assert.True(narrow.X > start.X);
        Assert.True(wide.X > narrow.X);
        Assert.True(moved >= 2);
        Assert.Equal(revision, doc.Revision); // nothing written yet
        Assert.Equal("line 1", client.SurroundingText); // the composition is not part of the text around the caret
        Assert.Equal(0, doc.UndoDepth);

        client.SetPreeditText(null); // cancelled
        Assert.Equal(start, client.CursorRectangle);
        Assert.Equal(revision, doc.Revision);

        client.SetPreeditText("にほんご");
        client.SetPreeditText(null); // what a platform does before it delivers the finished text
        _w.KeyTextInput("日本語");
        Assert.StartsWith("日本語line 1\n", Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, 32))));
        Assert.Equal(1, doc.UndoDepth);
        Assert.True(client.CursorRectangle.X > start.X);

        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Assert.StartsWith("line 1\nline 2\n", Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, 14))));
        Assert.Equal(0, doc.UndoDepth);
    }

    [AvaloniaFact]
    public void Composition_ends_when_the_caret_moves_and_reconversion_selects_text_in_the_line()
    {
        var tab = OpenIndexed(_temp.Bytes("reconvert.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")))));
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        tab.Paged!.RaiseEvent(args);
        var client = Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);

        client.SetPreeditText("ab");
        TestApp.Press(_w, Key.Right);
        var after = client.CursorRectangle;
        client.SetPreeditText(null);
        Assert.Equal(after, client.CursorRectangle); // the composition had been dropped when the caret moved

        client.Selection = new Avalonia.Input.TextInput.TextSelection(0, 4);
        Assert.Equal(new ByteRange(0, 4), tab.Paged.SelectionRange);
        _w.KeyTextInput("LINE");
        Assert.StartsWith("LINE 1\n", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, 7))));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
    }

    // ---- the line index in the background ----

    /// <summary>Byte offset where the row <paramref name="row"/> (zero based) of "line N\n" rows starts.</summary>
    private static long RowStart(long row)
    {
        long bytes = 0;
        for (long digits = 1, first = 1; first <= row; digits++, first *= 10)
        {
            long last = Math.Min(row, first * 10 - 1);
            bytes += (last - first + 1) * (5 + digits + 1);
        }
        return bytes;
    }

    private static void WriteRows(string path, long rows)
    {
        using var stream = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
        stream.NewLine = "\n";
        for (long i = 1; i <= rows; i++) stream.WriteLine("line " + i);
    }

    /// <summary>Holds the index of the file at <paramref name="gateBlock"/> blocks while jumps run, then lets it finish and checks the numbers again.</summary>
    private void JumpsWhileTheIndexIsBeingBuilt(long rows, int gateBlock, long farRow)
    {
        string path = System.IO.Path.Combine(_temp.Path, "rows.log");
        WriteRows(path, rows);
        Assert.Equal(new FileInfo(path).Length, RowStart(rows));
        using var gate = new ManualResetEventSlim();
        MappedFileSource.IndexBlockHook = (source, block) => { if (source.Path == path && block >= gateBlock) gate.Wait(); };
        try
        {
            var tab = Open(path);
            var paged = tab.Paged!;
            var document = PagedOf(tab);
            long prefix = (long)gateBlock * Mib;
            WaitFor(() => document.IndexedBytes == prefix, "index held at the gate");
            Assert.False(paged.Index.Complete);

            // the prefix is browsable while the index runs, with exact numbers
            using var surface = new PagedTextView.TextSurface(paged);
            Assert.StartsWith("line 1\nline 2\n", surface.GetText(0, Math.Min(surface.Length, 40)));
            Assert.True(paged.LinesExact);
            Assert.True(paged.GoToLine(100));
            Assert.Equal((99L, 0L), paged.CaretLineColumn());
            Assert.Contains("100", _w.StatusLeftText);
            Assert.DoesNotContain("≈", _w.StatusLeftText);

            // a jump to an offset inside the prefix is exact, in the middle of a line too
            long inside = RowStart(5000) + 2;
            paged.GoToOffset(inside);
            Assert.True(paged.LinesExact);
            Assert.Equal(inside, paged.CaretOffset);
            Assert.Equal((5000L, 2L), paged.CaretLineColumn());

            // a line the index has not reached cannot be found yet; nothing moves
            Assert.False(paged.GoToLine(farRow + 1));
            Assert.Equal(inside, paged.CaretOffset);
            _w.GoTo((farRow + 1).ToString());
            Assert.Equal(Loc.Get(K.GoToNotIndexed), _w.StatusMessageText);

            // a jump beyond the prefix lands on the right bytes; the line number is an estimate marked with "≈"
            long far = RowStart(farRow) + 3;
            Assert.True(far > prefix);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            _w.GoTo("@" + far);
            Assert.True(timer.ElapsedMilliseconds < 2000, "the jump waits for the index");
            _w.UpdateLayout();
            Assert.False(paged.LinesExact);
            Assert.Equal(far, paged.CaretOffset);
            Assert.Contains("≈", _w.StatusLeftText);
            Assert.StartsWith("lin", surface.GetText(surface.CaretOffset - 3, 3));
            long estimated = paged.CaretLineColumn()!.Value.Line;
            Assert.InRange(estimated, farRow * 98 / 100, farRow * 102 / 100);

            // moving on in the detour reads the following lines
            TestApp.Press(_w, Key.Down);
            Assert.Equal(RowStart(farRow + 1) + 3, paged.CaretOffset);
            Assert.Equal($"line {farRow + 2}\n", LineAtCaret(surface));

            // editing waits for the index
            TestApp.Type(_w, "x");
            Assert.False(tab.Doc.Doc.IsModified);

            // the index finishes: the view keeps its place and the numbers become exact
            long caret = paged.CaretOffset;
            gate.Set();
            WaitFor(() => paged.Index.Complete && paged.LinesExact, "exact numbers after the index");
            Assert.Equal(caret, paged.CaretOffset);
            Assert.Equal((farRow + 1, 3L), paged.CaretLineColumn());
            Assert.Equal($"line {farRow + 2}\n", LineAtCaret(surface));
            Assert.DoesNotContain("≈", _w.StatusLeftText);
            Assert.Contains($"{farRow + 2}", _w.StatusLeftText);

            // and the jumps agree with the naive calculation
            Assert.True(paged.GoToLine(farRow));
            Assert.Equal(RowStart(farRow - 1), paged.CaretOffset);
            paged.GoToOffset(RowStart(rows - 1) + 1);
            Assert.Equal((rows - 1, 1L), paged.CaretLineColumn());
        }
        finally
        {
            gate.Set();
            MappedFileSource.IndexBlockHook = null;
        }
    }

    private static string LineAtCaret(PagedTextView.TextSurface surface)
    {
        var range = TextRange.Caret(surface);
        range.ExpandToEnclosingUnit(TextUnit.Line);
        return range.GetText();
    }

    [AvaloniaFact]
    public void Jumps_to_an_offset_and_a_line_work_while_the_index_is_being_built()
    {
        JumpsWhileTheIndexIsBeingBuilt(rows: 700_000, gateBlock: 3, farRow: 500_000);
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void Jumps_while_a_large_file_is_being_indexed_hit_the_naive_offset_and_number()
    {
        JumpsWhileTheIndexIsBeingBuilt(rows: 30_000_000, gateBlock: 40, farRow: 20_000_000);
    }
}

using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

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

    // ---- very long lines and a bounded window ----

    private const string Unit = "ěšč abc 中";

    private static string Doc(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    [AvaloniaFact]
    public void A_caret_far_inside_a_long_line_is_read_where_it_is_and_edits_are_announced()
    {
        string model = string.Concat(Enumerable.Repeat(Unit, 150_000)); // 1 500 000 characters, far beyond the decoded part of the line
        var tab = OpenIndexed(_temp.Bytes("long-read.txt", Encoding.UTF8.GetBytes(model + "\nnext\n")));
        var paged = tab.Paged!;
        int col = 60_003;
        paged.GoToOffset(Encoding.UTF8.GetByteCount(model.AsSpan(0, col)));
        _w.UpdateLayout();
        using var surface = new PagedTextView.TextSurface(paged);
        var changes = new List<TextChange>();
        surface.TextChanged += changes.Add;

        Assert.InRange(surface.Length, 1, 4 * PagedTextView.MaxLineBytes); // the window never holds the line
        Assert.Equal(model[col], surface.GetText(surface.CaretOffset, 1)[0]);
        Assert.True(surface.Selection.IsEmpty);

        // the screen reader reads the character, the word and the selection at the caret
        var range = TextRange.Caret(surface);
        range.ExpandToEnclosingUnit(TextUnit.Character);
        Assert.Equal(model[col].ToString(), range.GetText());
        for (int i = 0; i < 3; i++) TestApp.Press(_w, Key.Right, RawInputModifiers.Shift);
        Assert.Equal(model.Substring(col, 3), TextRange.SelectionOf(surface).GetText());
        Assert.Equal(surface.Selection.End, surface.CaretOffset);
        Assert.Equal(model.Substring(col, 3), paged.SelectedText());
        var bounds = Assert.Single(surface.GetRangeBounds(surface.Selection.Start, 3));
        Assert.True(bounds.Width > 0);

        // typing over the selection is one change of the window text at the caret
        changes.Clear();
        TestApp.Type(_w, "Q");
        model = model[..col] + "Q" + model[(col + 3)..];
        col++;
        Assert.Equal(model, Doc(tab)[..model.Length]);
        Assert.NotEmpty(changes);
        Assert.Equal('Q', surface.GetText(surface.CaretOffset - 1, 1)[0]);
        Assert.Equal(model[col], surface.GetText(surface.CaretOffset, 1)[0]);

        // moving far along the line follows the caret; the window text changes and says so
        changes.Clear();
        paged.GoToOffset(Encoding.UTF8.GetByteCount(model.AsSpan(0, 90_000)));
        Assert.NotEmpty(changes);
        Assert.Equal(model[90_000], surface.GetText(surface.CaretOffset, 1)[0]);
        Assert.Contains(model.Substring(89_990, 20), surface.GetText(0, surface.Length));

        // back at the start of the line the first part is shown again
        paged.GoToOffset(0);
        Assert.StartsWith(model[..50], surface.GetText(0, surface.Length));
        Assert.Equal(0, surface.CaretOffset);
    }

    [AvaloniaFact]
    public void The_window_text_stays_bounded_when_every_line_is_long()
    {
        string row = new string('x', 15_000) + "\n";
        var tab = OpenIndexed(_temp.Bytes("long-rows.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(row, 800)))));
        var paged = tab.Paged!;
        _w.UpdateLayout();
        using var surface = new PagedTextView.TextSurface(paged);
        int changes = 0;
        surface.TextChanged += _ => changes++;

        void AssertBounded()
        {
            int visible = surface.VisibleRange.Length;
            Assert.True(visible > 0);
            Assert.True(surface.Length < 4 * PagedTextView.MaxLineBytes + 2 * (visible + 2 * row.Length), $"window of {surface.Length} characters");
            Assert.True(surface.Length < 800 * row.Length / 4);
        }

        AssertBounded();
        paged.GoToLine(400);
        AssertBounded();
        Assert.True(surface.FirstLine <= 399 && 399 < surface.FirstLine + surface.LineCount);
        changes = 0;
        for (int i = 0; i < 20; i++) _ = (surface.Length, surface.VisibleRange, surface.CaretOffset);
        Assert.Equal(0, changes); // a window that holds less than the margin does not rebuild itself on every query
    }

    [AvaloniaFact]
    public void Composition_on_a_long_line_reads_the_text_around_the_caret_and_the_commit_replaces_the_selection()
    {
        string model = string.Concat(Enumerable.Repeat(Unit, 150_000));
        var tab = OpenIndexed(_temp.Bytes("long-ime.txt", Encoding.UTF8.GetBytes(model + "\nnext\n")));
        var paged = tab.Paged!;
        int col = 70_000;
        paged.GoToOffset(Encoding.UTF8.GetByteCount(model.AsSpan(0, col)));
        _w.UpdateLayout();
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        paged.RaiseEvent(args);
        var client = Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);
        var doc = tab.Doc.Doc;
        long revision = doc.Revision;

        Assert.InRange(client.SurroundingText.Length, 1, 400);
        Assert.Contains(model.Substring(col, 10), client.SurroundingText);
        Assert.Equal(client.Selection.Start, client.Selection.End);
        int caretIndex = client.Selection.Start;
        Assert.Equal(model[col], client.SurroundingText[caretIndex]);

        client.SetPreeditText("にほんご");
        Assert.True(client.CursorRectangle.Width > 0);
        Assert.Equal(revision, doc.Revision);

        // reconversion: the input method sees the selection inside the text around the caret
        client.SetPreeditText(null);
        for (int i = 0; i < 4; i++) TestApp.Press(_w, Key.Right, RawInputModifiers.Shift);
        var selection = client.Selection;
        Assert.Equal(4, selection.End - selection.Start);
        Assert.Equal(model.Substring(col, 4), client.SurroundingText[selection.Start..selection.End]);

        _w.KeyTextInput("日本語");
        string expected = model[..col] + "日本語" + model[(col + 4)..];
        Assert.True(expected + "\nnext\n" == Doc(tab), "the document differs from the model");
        Assert.Equal(1, doc.UndoDepth);
        Assert.Equal((1L, (long)col + 3 + 1), tab.CaretPosition());

        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Assert.True(model + "\nnext\n" == Doc(tab), "undo did not restore the line");
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
            _w.GoTo("100:4");
            Assert.Equal(RowStart(99) + 3, paged.CaretOffset);
            Assert.Equal((99L, 3L), paged.CaretLineColumn());

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

            // a percentage beyond the prefix lands on that share of the bytes at once
            long length = new FileInfo(path).Length;
            _w.GoTo("90%");
            Assert.True(length * 90 / 100 > prefix);
            Assert.Equal(length * 90 / 100, paged.CaretOffset);
            Assert.False(paged.LinesExact);

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

    // ---- the view at a random offset (P3.1.6) ----

    private static string ViewRow(int i) => $"řádek {i} ěščř 日本 {i * 7}";

    /// <summary>
    /// A view built at a place of a large file draws the characters of the reference decoding of the bytes there, and reads a few blocks of the file: the block of the offset and
    /// its neighbours, not one block for each step of a search for the line that holds the offset.
    /// </summary>
    [AvaloniaFact]
    public void A_view_at_a_random_offset_shows_the_reference_text_and_reads_few_blocks()
    {
        _app.Workbench.Set("editor.minimap", JsonValue.Create(false)); // the map reads the file on its own
        const int rows = 2_000_000;
        var starts = new long[rows + 1];
        string path = System.IO.Path.Combine(_temp.Path, "viewport.txt");
        using (var stream = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20) { NewLine = "\n" })
            for (int i = 0; i < rows; i++)
            {
                string row = ViewRow(i);
                stream.WriteLine(row);
                starts[i + 1] = starts[i] + Encoding.UTF8.GetByteCount(row) + 1;
            }
        var tab = OpenIndexed(path);
        var paged = tab.Paged!;
        var file = PagedOf(tab).Source.File;
        Assert.True(file.Length > 60L << 20);

        var random = new Random(3106);
        for (int k = 0; k < 25; k++)
        {
            int row = random.Next(0, rows - 10), column = random.Next(0, 4);
            long offset = starts[row] + Encoding.UTF8.GetByteCount(ViewRow(row).AsSpan(0, column));
            file.BeginBlockCount();
            paged.GoToOffset(offset);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            int blocks = file.BlocksTouched;

            Assert.True(blocks <= PluginLimitsPlaceholders.PagedViewportMaxBlocks, $"{blocks} blocks at row {row}");
            Assert.Equal(offset, paged.CaretOffset);
            Assert.Equal(((long)row, (long)column), paged.CaretLineColumn());
            for (int i = 0; i < 4; i++) Assert.Equal(ViewRow(row + i), paged.DisplayOf(row + i));
        }
    }

    /// <summary>The same after edits: the line of an offset comes from the terminator counts of the piece tree, not from a search through the starts of lines that reads a block per step.</summary>
    [AvaloniaFact]
    public void A_view_at_a_random_offset_of_an_edited_file_shows_the_reference_text_and_reads_few_blocks()
    {
        _app.Workbench.Set("editor.minimap", JsonValue.Create(false));
        _app.History.Policy = new Core.History.HistoryPolicy { Enabled = false };
        const int rows = 1_300_000, editedRow = 1000;
        var starts = new long[rows + 1];
        string path = System.IO.Path.Combine(_temp.Path, "viewport-edited.txt");
        using (var stream = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20) { NewLine = "\n" })
            for (int i = 0; i < rows; i++)
            {
                string row = ViewRow(i);
                stream.WriteLine(row);
                starts[i + 1] = starts[i] + Encoding.UTF8.GetByteCount(row) + 1;
            }
        var tab = OpenIndexed(path);
        var paged = tab.Paged!;
        var file = PagedOf(tab).Source.File;
        Assert.True(file.Length > 40L << 20);

        paged.GoToOffset(starts[editedRow] + 2); // behind the first letter, which is two bytes
        TestApp.Type(_w, "Q"); // the next rows are one byte further on, and the tree has three pieces
        Assert.True(tab.Doc.Doc.IsModified);

        var random = new Random(3108);
        for (int k = 0; k < 25; k++)
        {
            int row = random.Next(editedRow + 2, rows - 10), column = random.Next(0, 4);
            long offset = starts[row] + 1 + Encoding.UTF8.GetByteCount(ViewRow(row).AsSpan(0, column));
            file.BeginBlockCount();
            paged.GoToOffset(offset);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            int blocks = file.BlocksTouched;

            Assert.True(blocks <= PluginLimitsPlaceholders.PagedViewportMaxBlocks, $"{blocks} blocks at row {row}");
            Assert.Equal(offset, paged.CaretOffset);
            Assert.Equal(((long)row, (long)column), paged.CaretLineColumn());
            for (int i = 0; i < 4; i++) Assert.Equal(ViewRow(row + i), paged.DisplayOf(row + i));
        }
    }

    /// <summary>
    /// A jump beyond the indexed part into a line of many megabytes: the view looks a bounded stretch back for the start of the line and forward for its end, and cuts the line there,
    /// so the jump reads a few blocks and not the whole line.
    /// </summary>
    [AvaloniaFact]
    public void A_jump_into_a_giant_line_beyond_the_index_looks_at_a_bounded_stretch_of_it()
    {
        _app.Workbench.Set("editor.minimap", JsonValue.Create(false));
        const int scan = 64 * 1024, size = 40 * Mib;
        var content = new byte[size + 12];
        Array.Fill(content, (byte)'x', 0, size);
        "\ntail\nmore\n"u8.CopyTo(content.AsSpan(size));
        string path = _temp.Bytes("giant.txt", content);
        using var gate = new ManualResetEventSlim();
        MappedFileSource.IndexBlockHook = (source, block) => { if (source.Path == path && block >= 1) gate.Wait(); };
        try
        {
            var tab = Open(path);
            var paged = tab.Paged!;
            paged.DetourLineScanBytes = scan;
            WaitFor(() => PagedOf(tab).IndexedBytes == Mib, "index held at the gate");
            var file = PagedOf(tab).Source.File;

            long caret = 20L * Mib + 1000;
            file.BeginBlockCount();
            paged.GoToOffset(caret);
            int jumped = file.BlocksTouched;
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            int blocks = file.BlocksTouched;
            Assert.True(blocks <= PluginLimitsPlaceholders.PagedViewportMaxBlocks, $"{blocks} blocks for the jump into a line of {size >> 20} MiB ({jumped} before the draw): {string.Join(',', file.BlockNumbersTouched())}");
            Assert.False(paged.LinesExact);
            Assert.Equal(caret, paged.CaretOffset);

            // down is the next view line at the same column, one scan further on, and up is the way back
            var before = paged.CaretLineColumn();
            TestApp.Press(_w, Key.Down);
            Assert.True(caret + scan == paged.CaretOffset, $"down went from {caret} at {before} to {paged.CaretOffset} at {paged.CaretLineColumn()}, a view line is {scan} bytes");
            TestApp.Press(_w, Key.Up);
            Assert.Equal(caret, paged.CaretOffset);
        }
        finally
        {
            gate.Set();
            MappedFileSource.IndexBlockHook = null;
        }
    }

    /// <summary>The same at gigabytes: 30 million rows of "line N", about 300 MiB, so the search for a line would take nine blocks and more.</summary>
    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void A_view_at_a_random_offset_of_a_large_file_reads_few_blocks()
    {
        _app.Workbench.Set("editor.minimap", JsonValue.Create(false));
        const long rows = 30_000_000;
        string path = System.IO.Path.Combine(_temp.Path, "rows.log");
        WriteRows(path, rows);
        var tab = OpenIndexed(path);
        var paged = tab.Paged!;
        var file = PagedOf(tab).Source.File;
        var random = new Random(3107);
        for (int k = 0; k < 40; k++)
        {
            long row = random.NextInt64(1, rows - 10);
            file.BeginBlockCount();
            paged.GoToOffset(RowStart(row) + 2);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            int blocks = file.BlocksTouched;

            Assert.True(blocks <= PluginLimitsPlaceholders.PagedViewportMaxBlocks, $"{blocks} blocks at row {row}");
            Assert.Equal((row, 2L), paged.CaretLineColumn());
            Assert.Equal("line " + (row + 1), paged.DisplayOf(row));
        }
    }

    /// <summary>"ĊĊ" is U+010A twice: the byte 0A, the line feed of UTF-8, lies inside it in either byte order, so a count of 0A bytes finds three line ends per row instead of one.</summary>
    private const string WideRow = "ĊĊ řádek ";

    [AvaloniaTheory]
    [InlineData("utf-16", "\n")]
    [InlineData("utf-16BE", "\r")]
    [InlineData("utf-32", "\r\n")]
    [InlineData("utf-32BE", "\n")]
    public void Bytes_per_line_of_a_sample_are_counted_by_code_unit(string name, string eol)
    {
        var enc = Encoding.GetEncoding(name);
        var codec = name switch { "utf-16" => TextCodec.Utf16LE, "utf-16BE" => TextCodec.Utf16BE, "utf-32" => TextCodec.Utf32LE, _ => TextCodec.Utf32BE };
        string row = WideRow + "123456" + eol;
        byte[] sample = enc.GetBytes(string.Concat(Enumerable.Repeat(row, 2000)));

        Assert.Equal(enc.GetByteCount(row), PagedTextView.BytesPerLine(sample, codec), precision: 3);
    }

    /// <summary>A jump beyond the indexed part of a UTF-16 or UTF-32 file numbers the lines from the density of its line ends in code units, so the estimate is as good as for UTF-8.</summary>
    [AvaloniaTheory]
    [InlineData("utf-16", "\n")]
    [InlineData("utf-16BE", "\r")]
    [InlineData("utf-32", "\r\n")]
    [InlineData("utf-32BE", "\n")]
    public void A_jump_beyond_the_indexed_part_of_a_wide_file_estimates_the_line_by_code_units(string name, string eol)
    {
        const int rows = 150_000;
        const long farRow = 100_000;
        var enc = Encoding.GetEncoding(name);
        var text = new StringBuilder();
        int farChar = 0;
        for (int i = 0; i < rows; i++)
        {
            if (i == farRow) farChar = text.Length;
            text.Append(WideRow).Append(i.ToString("D6")).Append(eol);
        }
        string all = text.ToString();
        string path = _temp.Bytes("wide-rows.txt", [.. enc.GetPreamble(), .. enc.GetBytes(all)]);
        long far = enc.GetPreamble().Length + enc.GetByteCount(all.AsSpan(0, farChar)) + enc.GetByteCount("ĊĊ");
        using var gate = new ManualResetEventSlim();
        MappedFileSource.IndexBlockHook = (source, block) => { if (source.Path == path && block >= 1) gate.Wait(); };
        try
        {
            var tab = Open(path);
            var paged = tab.Paged!;
            WaitFor(() => PagedOf(tab).IndexedBytes == Mib, "index held at the gate");
            Assert.False(paged.Index.Complete);

            paged.GoToOffset(far);
            _w.UpdateLayout();
            Assert.False(paged.LinesExact);
            Assert.Equal(far, paged.CaretOffset);
            Assert.InRange(paged.CaretLineColumn()!.Value.Line, farRow * 99 / 100, farRow * 101 / 100);
            Assert.Contains("≈", _w.StatusLeftText);

            gate.Set();
            WaitFor(() => paged.Index.Complete && paged.LinesExact, "exact numbers after the index");
            Assert.Equal(far, paged.CaretOffset);
            Assert.Equal((farRow, 2L), paged.CaretLineColumn());
        }
        finally
        {
            gate.Set();
            MappedFileSource.IndexBlockHook = null;
        }
    }

    [AvaloniaFact]
    public void Go_to_a_line_and_column_counts_the_column_in_characters_of_the_line()
    {
        const string head = "první\nžluťoučký kůň\n😀ab\n";
        // filler of "x\n" pairs reads as UTF-16 LE code units and the file would be detected as one UTF-16 line
        var tab = OpenIndexed(_temp.Bytes("columns.txt", Encoding.UTF8.GetBytes(head + string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")))));
        var paged = tab.Paged!;
        _w.GoTo("2:5");
        Assert.Equal(Encoding.UTF8.GetByteCount("první\nžluť"), paged.CaretOffset);
        Assert.Equal((1L, 4L), paged.CaretLineColumn());
        _w.GoTo("2:99"); // past the end of the line: its end
        Assert.Equal(Encoding.UTF8.GetByteCount("první\nžluťoučký kůň"), paged.CaretOffset);
        _w.GoTo("3:2"); // inside the surrogate pair: in front of the emoji
        Assert.Equal(Encoding.UTF8.GetByteCount("první\nžluťoučký kůň\n"), paged.CaretOffset);
        _w.GoTo("3:4");
        Assert.Equal(Encoding.UTF8.GetByteCount("první\nžluťoučký kůň\n😀a"), paged.CaretOffset);
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void Jumps_while_a_large_file_is_being_indexed_hit_the_naive_offset_and_number()
    {
        JumpsWhileTheIndexIsBeingBuilt(rows: 30_000_000, gateBlock: 40, farRow: 20_000_000);
    }
}

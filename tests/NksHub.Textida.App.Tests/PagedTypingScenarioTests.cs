using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.1.11 and P3.1.12 against a computation that never touches the document: a seeded script of keys (Czech words, Backspace, Delete, Enter, Tab, overwriting a selection, joins at
/// CRLF and LF breaks) is typed into windows of lines that straddle the block boundaries of the line index, with the caret checked after every step. The saved file is hashed against
/// a stream built from the row generator and the window texts, 500 steps of undo and redo are compared with the window text after each, and the undo of everything gives the original hash.
/// </summary>
[Collection("LocState")]
public class PagedTypingScenarioTests : IDisposable
{
    private const int WindowLines = 300;
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedTypingScenarioTests()
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

    /// <summary>One row of the file: ASCII or a log line with Czech letters, every seventh one ended by CR LF.</summary>
    private static string Row(long n, bool czech)
    {
        string end = n % 7 == 3 ? "\r\n" : "\n";
        return czech
            ? $"2026-10-26 12:{n % 60:D2}:{n * 7 % 60:D2} INFO příliš žluťoučký kůň #{n}{end}"
            : $"line {n:D9} the quick brown fox jumps over the lazy dog {n * 2654435761 % 1000003}{end}";
    }

    /// <summary>Writes rows until the file holds at least <paramref name="size"/> bytes; returns the number of rows.</summary>
    private static long WriteRows(string path, long size, bool czech)
    {
        long rows = 0, bytes = 0;
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new StreamWriter(fs, new UTF8Encoding(false), 1 << 20);
        for (; bytes < size; rows++)
        {
            string row = Row(rows, czech);
            writer.Write(row);
            bytes += Encoding.UTF8.GetByteCount(row);
        }
        return rows;
    }

    /// <summary>For each target offset the first row that starts 6000 bytes before it or later, with its byte offset; a window of <see cref="WindowLines"/> rows from there holds the target.</summary>
    private static List<(long Row, long Offset)> FindWindows(long[] targets, bool czech)
    {
        var found = new List<(long, long)>();
        long offset = 0;
        for (long row = 0; found.Count < targets.Length; row++)
        {
            if (offset >= targets[found.Count] - 6000) found.Add((row, offset));
            offset += Encoding.UTF8.GetByteCount(Row(row, czech));
        }
        return found;
    }

    /// <summary>Rows <see cref="FirstRow"/> and on, as the script changes them. The edits stay inside, so the first row never moves in the file.</summary>
    private sealed class Window
    {
        public Window(long firstRow, long offset, bool czech)
        {
            FirstRow = firstRow;
            Offset = offset;
            Text = string.Concat(Enumerable.Range(0, WindowLines).Select(i => Row(firstRow + i, czech)));
            OriginalBytes = Encoding.UTF8.GetByteCount(Text);
        }

        public long FirstRow { get; }
        /// <summary>Byte offset of the first row in the original file.</summary>
        public long Offset { get; }
        public int OriginalBytes { get; }
        public string Text { get; set; }
        /// <summary>The caret as an index into <see cref="Text"/>.</summary>
        public int Index { get; set; }

        /// <summary>Lines of the window: every one ends with a break.</summary>
        public int LineCount => Text.AsSpan().Count('\n');
        public long Bytes => Encoding.UTF8.GetByteCount(Text);

        public int LineStart(int line)
        {
            int at = 0;
            for (int i = 0; i < line; i++) at = Text.IndexOf('\n', at) + 1;
            return at;
        }

        public int StartOfLineAt(int index) => index == 0 ? 0 : Text.LastIndexOf('\n', index - 1) + 1;

        /// <summary>Index of the CR or LF that ends the line holding <paramref name="index"/>.</summary>
        public int BreakAt(int index) => index + Text.AsSpan(index).IndexOfAny('\r', '\n');

        public int LineLength(int line)
        {
            int start = LineStart(line);
            return BreakAt(start) - start;
        }

        /// <summary>One-based line and UTF-16 column of the caret, <paramref name="lineShift"/> lines of earlier windows added to the line of the first row.</summary>
        public (long Line, long Column) Position(long lineShift)
        {
            long line = FirstRow + lineShift;
            for (int i = 0; i < Index; i++)
                if (Text[i] == '\n') line++;
            return (line + 1, Index - StartOfLineAt(Index) + 1);
        }
    }

    /// <summary>The keys of one window and the same edits on its text.</summary>
    private sealed class Script(PagedTypingScenarioTests test, DocTab tab, Window window, long lineShift)
    {
        private static readonly string[] Words = ["Příliš", "žluťoučký", "kůň", "úpěl", "ďábelské", "ódy", "Řeřicha", "ABC", "x"];

        private void Check(string step)
        {
            var expected = window.Position(lineShift);
            Assert.True(expected == tab.CaretPosition(), $"{step}: the caret is at {tab.CaretPosition()}, expected {expected}");
        }

        public void GoTo(int line, int column)
        {
            window.Index = window.LineStart(line) + column;
            Assert.True(tab.GoToLine((int)(window.FirstRow + lineShift + line + 1), column + 1));
            Check($"go to {line}:{column}");
        }

        public void Type(string text)
        {
            test.Type(text);
            window.Text = window.Text.Insert(window.Index, text);
            window.Index += text.Length;
            Check("type " + text);
        }

        public void Back()
        {
            test.Press(Key.Back);
            int at = window.Index;
            int remove = at > window.StartOfLineAt(at) ? 1 : at >= 2 && window.Text[at - 2] == '\r' && window.Text[at - 1] == '\n' ? 2 : 1;
            window.Text = window.Text.Remove(at - remove, remove);
            window.Index -= remove;
            Check("backspace");
        }

        public void Delete()
        {
            test.Press(Key.Delete);
            int at = window.Index, end = window.BreakAt(at);
            int remove = at < end ? 1 : window.Text[end] == '\r' ? 2 : 1;
            window.Text = window.Text.Remove(at, remove);
            Check("delete");
        }

        public void Enter()
        {
            string newLine = tab.Doc.NewLine(null);
            test.Press(Key.Enter);
            window.Text = window.Text.Insert(window.Index, newLine);
            window.Index += newLine.Length;
            Check("enter");
        }

        public void Tab()
        {
            string spaces = new(' ', 4 - (window.Index - window.StartOfLineAt(window.Index)) % 4);
            test.Press(Key.Tab);
            window.Text = window.Text.Insert(window.Index, spaces);
            window.Index += spaces.Length;
            Check("tab");
        }

        /// <summary>Selects <paramref name="count"/> characters to the right and types one character over them (one undo step).</summary>
        public void Overwrite(int count, char c)
        {
            for (int i = 0; i < count; i++) test.Press(Key.Right, RawInputModifiers.Shift);
            test.Type(c.ToString());
            window.Text = window.Text.Remove(window.Index, count).Insert(window.Index, c.ToString());
            window.Index++;
            Check($"overwrite {count}");
        }

        /// <summary>A move to a random place of the window, which ends the typing run of the step before, and one random edit there: one undo step.</summary>
        public void RandomStep(Random random)
        {
            int line = random.Next(1, window.LineCount - 1); // a line before and after it stay in the window
            GoTo(line, random.Next(0, window.LineLength(line) + 1));
            switch (random.Next(7))
            {
                case 0 or 1: Type(Words[random.Next(Words.Length)]); break;
                case 2: Back(); break;
                case 3: Delete(); break;
                case 4: Enter(); break;
                case 5: Tab(); break;
                default: Overwrite(Math.Min(random.Next(1, 4), window.BreakAt(window.Index) - window.Index), 'Ž'); break;
            }
        }
    }

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Type(string text) => TestApp.Type(_w, text);

    private static string FileSha(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static string Sha(Document doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            hash.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>The hash of the file as the rows and the windows say it should be: no file and no document is read.</summary>
    private static string ExpectedSha(long rows, bool czech, IEnumerable<Window> windows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var ordered = windows.OrderBy(w => w.FirstRow).ToList();
        int next = 0;
        for (long row = 0; row < rows; row++)
        {
            if (next < ordered.Count && row == ordered[next].FirstRow)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(ordered[next++].Text));
                row += WindowLines - 1;
                continue;
            }
            hash.AppendData(Encoding.UTF8.GetBytes(Row(row, czech)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static long StablePrivateBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Process.GetCurrentProcess().PrivateMemorySize64;
    }

    private DocTab OpenIndexed(string path, int seconds)
    {
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        var clock = Stopwatch.StartNew();
        WaitIndexed(tab, clock, seconds);
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private static void WaitIndexed(DocTab tab, Stopwatch clock, int seconds)
    {
        while (!tab.Paged!.Index.Complete && clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
    }

    private async Task Scenario(long size, bool czech, long[] targets, int bigIndex, int steps, int seconds, bool measureMemory)
    {
        string path = Path.Combine(_temp.Path, "scenario.txt");
        long rows = WriteRows(path, size, czech);
        long fileLength = new FileInfo(path).Length;
        var windows = FindWindows(targets, czech).Select(s => new Window(s.Row, s.Offset, czech)).ToList();
        string originalSha = ExpectedSha(rows, czech, []);
        Assert.Equal(originalSha, FileSha(path)); // the generator is the file

        var tab = OpenIndexed(path, seconds);
        var doc = tab.Doc.Doc;
        long privateBefore = measureMemory ? StablePrivateBytes() : 0;

        // a short script in every window but the big one, then the long one, so that its steps are the last on the undo stack
        for (int i = 0; i < windows.Count; i++)
        {
            if (i == bigIndex) continue;
            var w = windows[i];
            var script = new Script(this, tab, w, windows.Where(o => o.FirstRow < w.FirstRow).Sum(o => (long)o.LineCount - WindowLines));
            var random = new Random(100 + i);
            for (int step = 0; step < 40; step++) script.RandomStep(random);
        }
        var big = windows[bigIndex];
        var snapshots = new List<string> { big.Text };
        var bigScript = new Script(this, tab, big, windows.Where(o => o.FirstRow < big.FirstRow).Sum(o => (long)o.LineCount - WindowLines));
        var bigRandom = new Random(7);
        for (int step = 0; step < steps; step++)
        {
            bigScript.RandomStep(bigRandom);
            snapshots.Add(big.Text);
        }
        Assert.Equal((windows.Count - 1) * 40 + steps, doc.UndoDepth); // every step, a typed word included, is one

        Assert.True(await _w.SaveAsync(tab));
        Assert.False(doc.IsModified);
        Assert.Equal(ExpectedSha(rows, czech, windows), FileSha(path));
        if (measureMemory)
        {
            long grown = StablePrivateBytes() - privateBefore;
            Assert.True(grown <= 350L << 20, $"private commit grew by {grown >> 20} MiB");
        }

        WaitIndexed(tab, Stopwatch.StartNew(), seconds);
        long bigOffset = big.Offset + windows.Where(o => o.FirstRow < big.FirstRow).Sum(o => o.Bytes - o.OriginalBytes);
        long lengthWithoutBig = fileLength + windows.Where(o => o != big).Sum(o => o.Bytes - o.OriginalBytes) - big.OriginalBytes;
        void AssertWindow(string expected, string step)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(expected);
            Assert.True(lengthWithoutBig + bytes.Length == doc.ByteLength, $"{step}: the file has {doc.ByteLength} bytes, expected {lengthWithoutBig + bytes.Length}");
            Assert.True(bytes.AsSpan().SequenceEqual(doc.ReadBytes(new ByteRange(bigOffset, bigOffset + bytes.Length))), $"{step}: the window differs");
        }
        for (int k = 1; k <= steps; k++)
        {
            Press(Key.Z, TestApp.Mod);
            AssertWindow(snapshots[steps - k], $"undo {k}");
        }
        for (int k = 1; k <= steps; k++)
        {
            Press(Key.Z, TestApp.Mod | RawInputModifiers.Shift);
            AssertWindow(snapshots[k], $"redo {k}");
        }

        // all the way back: the bytes of the original, and the next save writes the revision after the undo
        for (int k = 0; k < doc.UndoDepth + 1000 && doc.CanUndo; k++) Press(Key.Z, TestApp.Mod);
        Assert.False(doc.CanUndo);
        Assert.Equal(originalSha, Sha(doc));
        Assert.True(doc.IsModified);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal(originalSha, FileSha(path));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Scripted_keys_at_the_block_boundaries_of_a_3_MiB_file_save_to_the_independent_hash_and_undo_and_redo_in_exact_steps(bool czech) =>
        Scenario(3 << 20, czech, [128 << 10, 1 << 20, 2 << 20], bigIndex: 1, steps: 500, seconds: 60, measureMemory: false);

    [AvaloniaTheory]
    [Trait("Category", "Large")]
    [InlineData(false)]
    [InlineData(true)]
    public Task Scripted_keys_in_a_100_MiB_file_save_to_the_independent_hash_and_undo_and_redo_in_exact_steps(bool czech) =>
        Scenario(100L << 20, czech, [1 << 20, 50L << 20, (100L << 20) - (64 << 10)], bigIndex: 1, steps: 500, seconds: 600, measureMemory: !czech);

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public async Task The_largest_paste_into_a_100_MiB_file_ends_in_time_with_exact_lines_and_a_bounded_memory_and_is_one_undo_step()
    {
        string path = Path.Combine(_temp.Path, "paste.txt");
        long rows = WriteRows(path, 100L << 20, czech: false);
        var tab = OpenIndexed(path, 600);
        var view = tab.Paged!;
        var doc = tab.Doc.Doc;
        string originalSha = Sha(doc);
        long originalLines = view.Index.Lines;
        Assert.True(tab.GoToLine((int)(rows / 2), 11)); // in the middle of a row in the middle of the file

        long before = StablePrivateBytes();
        var builder = new StringBuilder((int)PluginLimitsPlaceholders.PagedPasteMaxChars + 200);
        for (long n = 0; builder.Length < PluginLimitsPlaceholders.PagedPasteMaxChars; n++) builder.Append($"pasted row {n:D9} with a few words to make the line longer\n");
        builder.Length = (int)PluginLimitsPlaceholders.PagedPasteMaxChars;
        string text = builder.ToString();
        builder.Clear();
        int breaks = text.AsSpan().Count('\n');

        var clock = Stopwatch.StartNew();
        Assert.True(await view.PasteTextAsync(text));
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds <= PluginLimitsPlaceholders.PagedPasteMeasureMilliseconds, $"the paste took {clock.ElapsedMilliseconds} ms");
        long grown = StablePrivateBytes() - before;
        GC.KeepAlive(text);
        Assert.True(grown <= 350L << 20, $"private commit grew by {grown >> 20} MiB");
        Assert.Equal(1, doc.UndoDepth);

        // the count of lines and the start of a sample of them against one pass over every byte of the edited document
        long lines = view.Index.Lines;
        Assert.Equal(originalLines + breaks, lines);
        var paged = ((PagedBackend)doc.Backend).Paged;
        long line = 0;
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            for (int i = 0; i < n; i++)
            {
                if (buffer[i] != (byte)'\n') continue;
                line++;
                if (line % 100_000 != 0 && line != lines - 1) continue;
                Assert.True(paged.TryGetLineStart(line, out long start));
                Assert.Equal(at + i + 1, start);
            }
            at += n;
        }
        Assert.Equal(lines - 1, line);

        Press(Key.Z, TestApp.Mod);
        Assert.Equal(originalSha, Sha(doc));
        Assert.Equal(0, doc.UndoDepth);
    }
}

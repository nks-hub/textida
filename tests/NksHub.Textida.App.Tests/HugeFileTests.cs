using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// The big-file conditions of P3.1.6, P3.1.8 and P9.4.2 on real files of 10 and 50 GiB, through the window of the app: a view at random places of a 10 GiB file, the window
/// while the index of a 50 GiB file is built and a jump ahead of it, the heatmap of a 10 GiB log. The files have fixed lines of 64 bytes (see <see cref="HugeText"/>), so every expected
/// offset, line number and text is a formula. The 10 GiB tests are Perf tests; the 50 GiB ones run only where <c>TEXTIDA_HUGE=1</c> is set and the disk has the room, and they are
/// meant for the test machine of the project, not for a developer PC. The numbers are written to the output of the test.
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public sealed class HugeFileTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HugeFileTests(ITestOutputHelper output)
    {
        _output = output;
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

    /// <summary>The big files of a run: written once, removed when the process ends (the test machine also clears its temp folder before every run).</summary>
    private static class Files
    {
        private static readonly string Dir = Directory.CreateTempSubdirectory("textida-huge-").FullName;
        private static readonly Dictionary<string, string> Made = [];

        static Files() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };

        public static string Get(string name, long size, Func<long, int>? levelOfBlock, ITestOutputHelper output)
        {
            lock (Made)
            {
                if (Made.TryGetValue(name, out var made)) return made;
                string path = Path.Combine(Dir, name);
                HugeText.RequireRoom(path, size + (2L << 30));
                var clock = Stopwatch.StartNew();
                HugeText.WriteFile(path, size, levelOfBlock);
                output.WriteLine($"{name}: {size / 1073741824.0:F1} GiB written in {clock.Elapsed.TotalSeconds:F0} s");
                return Made[name] = path;
            }
        }
    }

    private static PagedDocument PagedOf(DocTab tab) => ((PagedBackend)tab.Doc.Doc.Backend).Paged;

    private static void PumpUntil(Func<bool> done, string what, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < timeout)
        {
            Thread.Sleep(2);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), $"{what} (waited {clock.Elapsed.TotalSeconds:F0} s)");
    }

    /// <summary>Opens the file in the window like the app does and says how long the call held the window.</summary>
    private DocTab Open(string path, out double callMs)
    {
        var clock = Stopwatch.StartNew();
        var tab = _w.OpenFile(path, null);
        callMs = clock.Elapsed.TotalMilliseconds;
        Assert.True(tab.Doc.IsLarge);
        _w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private static double Percentile(List<double> sorted, double share) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * share))];

    private static string LineAtCaret(PagedTextView.TextSurface surface)
    {
        var range = TextRange.Caret(surface);
        range.ExpandToEnclosingUnit(TextUnit.Line);
        return range.GetText();
    }

    // ---- P3.1.6 ----

    /// <summary>
    /// A view put at random places of a 10 GiB file shows the characters of the reference decoding of the bytes there (here the line number and the letters of the generator) and reads
    /// at most <see cref="PluginLimitsPlaceholders.PagedViewportMaxBlocks"/> blocks of 1 MiB for it.
    /// </summary>
    [AvaloniaFact]
    public void A_view_at_a_random_place_of_a_10_GiB_file_shows_the_reference_text_and_reads_few_blocks()
    {
        _app.Workbench.Set("editor.minimap", JsonValue.Create(false)); // the map reads the file on its own
        long size = HugeText.Size(10L << 30);
        string path = Files.Get("view.txt", size, null, _output);

        var tab = Open(path, out double callMs); // the number is what P3.1.1 asks 1 s for
        var paged = tab.Paged!;
        var file = PagedOf(tab).Source.File;
        var clock = Stopwatch.StartNew();
        PumpUntil(() => paged.Index.Complete, "the index of the file", TimeSpan.FromMinutes(15));
        _output.WriteLine($"OpenFile held the window for {callMs:F0} ms (P3.1.1 asks for 1 s); the index was complete {clock.Elapsed.TotalSeconds:F0} s after that");
        if (callMs > 1000) Assert.Fail($"OpenFile held the window for {callMs:F0} ms");

        long lines = size / HugeText.LineBytes;
        var random = new Random(3109);
        var blocks = new List<int>();
        var problems = new List<string>();
        for (int k = 0; k < 40; k++)
        {
            long row = random.NextInt64(0, lines - 10);
            int column = random.Next(0, HugeText.LineBytes - 1);
            long offset = row * HugeText.LineBytes + column;
            file.BeginBlockCount();
            paged.GoToOffset(offset);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            blocks.Add(file.BlocksTouched);

            if (paged.CaretOffset != offset) problems.Add($"row {row}: the caret is at {paged.CaretOffset}, not {offset}");
            if (paged.CaretLineColumn() != (row, (long)column)) problems.Add($"row {row}: the caret is at {paged.CaretLineColumn()}");
            for (int i = 0; i < 4; i++)
                if (paged.DisplayOf(row + i) != HugeText.LineText(row + i)) problems.Add($"row {row + i} shows '{paged.DisplayOf(row + i)}'");
        }
        _output.WriteLine($"{blocks.Count} views of the {size >> 30} GiB file read {blocks.Min()} to {blocks.Max()} blocks of 1 MiB each (limit {PluginLimitsPlaceholders.PagedViewportMaxBlocks})");
        Assert.True(blocks.Max() <= PluginLimitsPlaceholders.PagedViewportMaxBlocks, $"a view read {blocks.Max()} blocks");
        Assert.True(problems.Count == 0, string.Join("; ", problems.Take(5)));
    }

    // ---- P9.4.2 ----

    /// <summary>A snapshot that counts the bytes read through it.</summary>
    private sealed class CountingSnapshot(IByteSnapshot inner) : IByteSnapshot
    {
        public long Bytes;
        public long ByteLength => inner.ByteLength;

        public int Read(long offset, Span<byte> destination)
        {
            int n = inner.Read(offset, destination);
            Interlocked.Add(ref Bytes, n);
            return n;
        }
    }

    /// <summary>The level of every line of a block of 1 MiB of the log of the heatmap tests: two clusters of errors and one of warnings in a log of INFO lines.</summary>
    private static int LevelOfBlock(long block, long blocks)
    {
        long run = Math.Max(4, blocks / 160);
        if (block >= blocks / 10 && block < blocks / 10 + run) return 2;
        if (block >= blocks * 6 / 10 && block < blocks * 6 / 10 + 2 * run) return 2;
        if (block >= blocks * 3 / 10 && block < blocks * 3 / 10 + run) return 1;
        return 0;
    }

    /// <summary>
    /// The heatmap of the log in the strip of the minimap puts the clusters of errors and of warnings of a 10 GiB log where they are and nowhere else, and it is ready in
    /// seconds because only a window at the start of each bucket is read.
    /// </summary>
    [AvaloniaFact]
    public void The_heatmap_of_a_10_GiB_log_shows_its_clusters_of_errors_and_warnings_where_they_are()
    {
        long size = HugeText.Size(10L << 30), blocksInFile = size / HugeText.BlockBytes;
        string path = Files.Get("heat.log", size, b => LevelOfBlock(b, blocksInFile), _output);

        var tab = Open(path, out double callMs);
        Assert.True(tab.Doc.IsLog);
        var clock = Stopwatch.StartNew();
        PumpUntil(() => tab.Minimap?.Heat is not null, "the heatmap", TimeSpan.FromMinutes(10));
        double heatSeconds = clock.Elapsed.TotalSeconds;
        var heat = tab.Minimap!.Heat!;
        Assert.True(heat.Sampled);

        // the same count again, now to see how much of the file it reads
        clock.Restart();
        var snapshot = tab.Doc.Doc.GetSnapshot();
        var counting = new CountingSnapshot(snapshot);
        var again = LogHeat.Compute(counting, snapshot.Codec, tab.Doc.Doc.BomLength, tab.Doc.LogLevels, PluginLimitsPlaceholders.LogHeatBuckets, PluginLimitsPlaceholders.LogHeat());
        double againSeconds = clock.Elapsed.TotalSeconds;
        long read = Interlocked.Read(ref counting.Bytes);

        int buckets = heat.Buckets, errorBuckets = 0, warningBuckets = 0, judged = 0;
        var problems = new List<string>();
        for (int b = 0; b < buckets; b++)
        {
            long from = (size * b / buckets + HugeText.LineBytes - 1) / HugeText.LineBytes * HugeText.LineBytes; // the first line that starts in the bucket
            long first = from / HugeText.BlockBytes, last = (from + PluginLimitsPlaceholders.LogHeatSampleBytesPerBucket - 1) / HugeText.BlockBytes;
            int level = LevelOfBlock(first, blocksInFile);
            if (level != LevelOfBlock(last, blocksInFile)) continue; // the window of this bucket crosses the edge of a cluster
            judged++;
            double error = heat.ErrorDensity(b), warning = heat.WarningDensity(b);
            if (level == 2) errorBuckets++;
            if (level == 1) warningBuckets++;
            if (Math.Abs(error - (level == 2 ? 1 : 0)) > 1e-9 || Math.Abs(warning - (level == 1 ? 1 : 0)) > 1e-9)
                problems.Add($"bucket {b}: error {error:F3}, warning {warning:F3}, expected level {level}");
        }
        _output.WriteLine($"OpenFile held the window for {callMs / 1000:F1} s; the heatmap of {size / 1073741824.0:F1} GiB was in the minimap {heatSeconds:F1} s later; counted again in {againSeconds:F1} s reading {read >> 20} MiB of the {size >> 20} MiB");
        _output.WriteLine($"{judged} of {buckets} buckets judged: {errorBuckets} in the clusters of errors, {warningBuckets} in the cluster of warnings, the rest quiet; {problems.Count} wrong");
        Assert.True(errorBuckets > 0 && warningBuckets > 0, "the test log has no cluster that a bucket falls into");
        Assert.True(problems.Count == 0, string.Join("; ", problems.Take(5)));
        // per bucket: the window, and the two looks (64 KiB each) for the start of the first whole line at its two ends
        long allowed = (long)buckets * (PluginLimitsPlaceholders.LogHeatSampleBytesPerBucket + 2 * 64 * 1024) * 21 / 20;
        Assert.True(read <= allowed, $"the count read {read >> 20} MiB, a window of {PluginLimitsPlaceholders.LogHeatSampleBytesPerBucket >> 10} KiB per bucket allows {allowed >> 20} MiB");
        Assert.Equal(heat.Errors, again.Errors);
        Assert.Equal(heat.Lines, again.Lines);
    }

    // ---- P8b.3.3 and P8b.3.5 ----

    /// <summary>
    /// The window of a transcript of 1.2 GiB with a result of 54 MiB in the middle shows its first turn within 1 s (2 s with the file out of the file cache, which needs the disk of the
    /// test machine and so <c>TEXTIDA_HUGE=1</c>); the list of turns scrolls within the limits of PR.2.2 while it fills, the turn with the giant line is shown without reading the giant line, and the
    /// index stays within its memory limit.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_transcript_window_of_a_1_2_GiB_transcript_shows_its_first_turn_in_time_and_the_index_keeps_to_its_limit(bool cold)
    {
        if (cold && !HugeText.Enabled)
        {
            _output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        string dir = Directory.CreateTempSubdirectory("textida-transcript-").FullName;
        try
        {
            string path = Path.Combine(dir, "big.jsonl");
            var clock = Stopwatch.StartNew();
            long lines = TranscriptFile.Write(path, out long giantLine);
            _output.WriteLine($"{new FileInfo(path).Length >> 20} MiB, {lines} lines written in {clock.Elapsed.TotalSeconds:F1} s");
            if (cold)
            {
                clock.Restart();
                long pushed = HugeText.EvictFileCache(dir);
                _output.WriteLine($"{pushed >> 30} GiB written and read to push the transcript out of the file cache, {clock.Elapsed.TotalSeconds:F0} s");
            }

            // the first window of a process pays for its start-up; that is not what the limit is about
            string tiny = Path.Combine(dir, "tiny.jsonl");
            File.WriteAllText(tiny, "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"hello\"}}\n");
            var warmUp = _w.OpenTranscript(tiny)!;
            PumpUntil(() => warmUp.TurnList.SelectedIndex >= 0 && warmUp.RowData.Count > 0, "the warm-up turn", TimeSpan.FromSeconds(30));
            warmUp.Close();

            clock.Restart();
            var window = _w.OpenTranscript(path) ?? throw new InvalidOperationException("the transcript did not open");
            PumpUntil(() => window.TurnList.SelectedIndex >= 0 && window.RowData.Count > 0, "the first turn", TimeSpan.FromSeconds(30));
            var first = clock.Elapsed;
            _output.WriteLine($"{(cold ? "cold" : "warm")}: the first turn was in the window {first.TotalMilliseconds:F0} ms after the call; status '{window.StatusText}'");

            // the list scrolls while it fills and after
            var origin = window.TurnList.TranslatePoint(default, window)!.Value;
            var inside = new Point(origin.X + 60, origin.Y + 60);
            List<double> whileFilling = [], afterwards = [];
            for (int step = 0; step < 400; step++)
            {
                var timer = Stopwatch.StartNew();
                window.MouseWheel(inside, new Vector(0, step % 40 < 30 ? -3 : 3), RawInputModifiers.None);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                (window.IsListComplete ? afterwards : whileFilling).Add(timer.Elapsed.TotalMilliseconds);
                Thread.Sleep(10);
            }
            PumpUntil(() => window.IsListComplete, "the end of the index", TimeSpan.FromMinutes(2));
            var index = window.Index;
            _output.WriteLine($"index complete: {index.LineCount} lines, {index.TurnCount} turns, {index.IndexBytes >> 10} KiB of {PluginLimitsPlaceholders.TranscriptIndexMaxBytes >> 10} KiB allowed, truncated {index.IsTruncated}");

            // the turn with the giant line: shown, with the line counted as too long to show whole
            int giantTurn = index.TurnOf(giantLine);
            clock.Restart();
            window.TurnList.SelectedIndex = giantTurn;
            var shown = window.TurnTask;
            PumpUntil(() => shown.IsCompleted && window.ShownTurn == giantTurn, "the turn with the giant line", TimeSpan.FromSeconds(30));
            double giantMs = clock.Elapsed.TotalMilliseconds;
            _output.WriteLine($"the turn with the 54 MiB line was shown in {giantMs:F0} ms; notice '{window.NoticeText}'");

            var problems = new List<string>();
            if (first > TimeSpan.FromSeconds(cold ? 2 : 1)) problems.Add($"the first turn took {first.TotalSeconds:F2} s");
            foreach (var (what, times) in new[] { ("the list of turns while it fills", whileFilling), ("the list of turns after", afterwards) })
            {
                times.Sort();
                if (times.Count == 0) { _output.WriteLine($"no wheel step was measured for {what}"); continue; }
                double p95 = Percentile(times, 0.95), p99 = Percentile(times, 0.99);
                _output.WriteLine($"{times.Count} wheel steps on {what}: p50 {Percentile(times, 0.5):F1} ms, p95 {p95:F1} ms, p99 {p99:F1} ms, max {times[^1]:F1} ms (limits p95 33, p99 80)");
                if (times.Count < 30) { _output.WriteLine("too few steps for a percentile"); continue; }
                if (p95 > 33) problems.Add($"{what}: p95 {p95:F1} ms");
                if (p99 > 80) problems.Add($"{what}: p99 {p99:F1} ms");
            }
            if (index.Error is not null) problems.Add("the index failed: " + index.Error.Message);
            if (index.IsTruncated) problems.Add("the index was cut off");
            if (index.LineCount != lines) problems.Add($"the index counted {index.LineCount} lines of {lines}");
            if (index.IndexBytes > PluginLimitsPlaceholders.TranscriptIndexMaxBytes) problems.Add($"the index uses {index.IndexBytes} bytes");
            if (window.Index.ReadTurn(giantTurn).OversizedLines != 1) problems.Add("the giant line was not counted as oversized");
            Assert.True(problems.Count == 0, string.Join("; ", problems));
            window.Close();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // ---- P3.1.8 ----

    private static string Sample(Process self, Stopwatch clock, PagedTextView paged, PagedDocument document, ref long peakPrivate, ref long peakSet)
    {
        self.Refresh();
        peakPrivate = Math.Max(peakPrivate, self.PrivateMemorySize64);
        peakSet = Math.Max(peakSet, self.WorkingSet64);
        return $"{clock.Elapsed.TotalSeconds,5:F0} s  indexed {document.IndexedBytes / 1073741824.0,5:F1} GiB  private {self.PrivateMemorySize64 >> 20} MiB  working set {self.WorkingSet64 >> 20} MiB  mapped {paged.MappedResidentBytes >> 20} MiB";
    }

    /// <summary>
    /// The window while the index of a 50 GiB file is built, from the moment the open call returns: PageDown and PageUp are timed against the limits of PR.2.2 (p95 33 ms, p99 80 ms),
    /// a jump past the finished part of the index lands on the byte and the line of a naive calculation with "≈" on the number until the index reaches it, then the numbers are exact and the
    /// caret has not moved. Private memory and working set are written every 10 seconds (P3.1.9).
    /// </summary>
    [AvaloniaFact]
    public void While_a_50_GiB_file_is_indexed_the_window_keeps_up_and_a_jump_ahead_of_the_index_lands_on_the_right_line()
    {
        if (!HugeText.Enabled)
        {
            _output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        long size = HugeText.Size(50L << 30), lines = size / HugeText.LineBytes;
        string path = Files.Get("index.txt", size, null, _output);

        TypingTrace.Reset();
        TypingTrace.Enabled = true;
        var tab = Open(path, out double callMs);
        var paged = tab.Paged!;
        var document = PagedOf(tab);
        var clock = Stopwatch.StartNew();
        var self = Process.GetCurrentProcess();
        var origin = paged.TranslatePoint(default, _w)!.Value;
        var inside = new Point(origin.X + 200, origin.Y + 100);
        long peakPrivate = 0, peakSet = 0, atOpen = document.IndexedBytes;
        _output.WriteLine($"OpenFile held the window for {callMs / 1000:F0} s; then {atOpen / 1073741824.0:F1} of {size / 1073741824.0:F1} GiB were indexed and the machine has {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 20} MiB of memory");

        var problems = new List<string>();
        List<double> scroll = [], scrollAhead = [];
        var targetLine = lines * 9 / 10;
        long far = targetLine * HugeText.LineBytes + 5;
        bool jumped = false;
        double jumpMs = 0;
        var next = TimeSpan.Zero;
        long stepStart = 0;
        using var watchdog = new Timer(_ =>
        {
            long started = Interlocked.Read(ref stepStart);
            if (started != 0 && Environment.TickCount64 - started > 3000)
            {
                HugeText.Log($"a step has been running for {(Environment.TickCount64 - started) / 1000} s; pool threads {ThreadPool.ThreadCount}, queued {ThreadPool.PendingWorkItemCount}; indexed {document.IndexedBytes >> 20} MiB");
                HugeText.Log("spans so far (ms):\n" + string.Join('\n', TypingTrace.Summary().Split('\n').Take(7)));
            }
        }, null, 5000, 10000);
        for (int step = 0; !paged.Index.Complete && clock.Elapsed < TimeSpan.FromMinutes(25); step++)
        {
            Interlocked.Exchange(ref stepStart, Environment.TickCount64);
            var timer = Stopwatch.StartNew();
            // before the jump the keys move the view and the caret from the top of the file; after it the wheel moves the view around the target and the caret stays on it
            if (!jumped) TestApp.Press(_w, step % 80 < 60 ? Key.PageDown : Key.PageUp);
            else _w.MouseWheel(inside, new Vector(0, step % 80 < 60 ? -3 : 3), RawInputModifiers.None);
            double pressMs = timer.Elapsed.TotalMilliseconds;
            _w.UpdateLayout();
            double layoutMs = timer.Elapsed.TotalMilliseconds - pressMs;
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed.TotalMilliseconds > 100)
                HugeText.Log($"step {step}: key {pressMs:F0} ms, layout {layoutMs:F0} ms, posted jobs {timer.Elapsed.TotalMilliseconds - pressMs - layoutMs:F0} ms; pool threads {ThreadPool.ThreadCount}, queued {ThreadPool.PendingWorkItemCount}; indexed {document.IndexedBytes >> 20} MiB");
            Interlocked.Exchange(ref stepStart, 0);
            if (step >= 30) (jumped ? scrollAhead : scroll).Add(timer.Elapsed.TotalMilliseconds);
            if (clock.Elapsed >= next)
            {
                next += TimeSpan.FromSeconds(10);
                _output.WriteLine(Sample(self, clock, paged, document, ref peakPrivate, ref peakSet));
            }
            if (!jumped && document.IndexedBytes >= size / 5 && document.IndexedBytes < far)
            {
                jumped = true;
                long prefix = document.IndexedBytes;
                // inside the finished part a line is found exactly
                if (!paged.GoToLine(1000) || paged.CaretOffset != 999L * HugeText.LineBytes || !paged.LinesExact) problems.Add("a line inside the indexed part was not found exactly");
                // a line the index has not reached is refused
                if (paged.GoToLine(targetLine + 1)) problems.Add("a line beyond the index was accepted");
                timer.Restart();
                paged.GoToOffset(far);
                _w.UpdateLayout();
                jumpMs = timer.Elapsed.TotalMilliseconds;
                var at = paged.CaretLineColumn();
                using var surface = new PagedTextView.TextSurface(paged);
                if (paged.CaretOffset != far) problems.Add($"the jump landed on byte {paged.CaretOffset}, not {far}");
                if (paged.LinesExact) problems.Add("the line number after the jump is not marked as an estimate");
                if (!_w.StatusLeftText.Contains('≈')) problems.Add("the status line has no ≈: " + _w.StatusLeftText);
                if (at is not { } place || Math.Abs(place.Line - targetLine) > targetLine / 50) problems.Add($"the estimated line {at?.Line} is not within 2 % of {targetLine}");
                if (LineAtCaret(surface) != HugeText.LineText(targetLine) + "\n") problems.Add($"the line at the caret is '{LineAtCaret(surface)}'");
                _output.WriteLine($"jump to byte {far} with {prefix / 1073741824.0:F1} GiB indexed: {jumpMs:F0} ms, line shown {at?.Line} ≈ against {targetLine} ({(at is { } p ? (p.Line - targetLine) * 100.0 / targetLine : 0):F2} %), status '{_w.StatusLeftText}'");
            }
            Thread.Sleep(25);
        }
        double indexSeconds = callMs / 1000 + clock.Elapsed.TotalSeconds;
        if (!paged.Index.Complete) problems.Add("the index did not finish in 25 minutes");
        if (!jumped) problems.Add($"the index was already past the target ({atOpen / 1073741824.0:F1} GiB at open, {document.IndexedBytes / 1073741824.0:F1} GiB at the first step), so no jump ahead of it was possible");
        _output.WriteLine($"index complete {indexSeconds:F0} s after the start of the open call, {clock.Elapsed.TotalSeconds:F0} s after it returned; private memory peak {peakPrivate >> 20} MiB, working set peak {peakSet >> 20} MiB");

        if (jumped)
        {
            PumpUntil(() => paged.LinesExact, "the exact line numbers after the index", TimeSpan.FromMinutes(2));
            if (paged.CaretOffset != far) problems.Add($"the caret moved to {paged.CaretOffset} when the index caught up");
            if (paged.CaretLineColumn() != (targetLine, 5L)) problems.Add($"the exact position is {paged.CaretLineColumn()}, not ({targetLine}, 5)");
            if (_w.StatusLeftText.Contains('≈')) problems.Add("the ≈ stayed after the index was complete");
            if (!paged.GoToLine(targetLine + 1) || paged.CaretOffset != targetLine * HugeText.LineBytes) problems.Add("the jump to the line after the index landed elsewhere");
        }
        TestApp.Press(_w, Key.End, TestApp.Mod);
        if (paged.CaretOffset != size) problems.Add($"Ctrl+End landed on byte {paged.CaretOffset}, not {size}");

        // the same keys with the index finished: what the steps above cost on this machine when no worker competes with them
        HugeText.Log("spans while the index ran (ms):\n" + string.Join('\n', TypingTrace.Summary().Split('\n').Take(10)));
        TypingTrace.Reset();
        TestApp.Press(_w, Key.Home, TestApp.Mod);
        List<double> finished = [];
        for (int step = 0; step < 150; step++)
        {
            var timer = Stopwatch.StartNew();
            TestApp.Press(_w, step % 80 < 60 ? Key.PageDown : Key.PageUp);
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (step >= 30) finished.Add(timer.Elapsed.TotalMilliseconds);
            Thread.Sleep(25);
        }
        finished.Sort();
        _output.WriteLine($"{finished.Count} PageDown/PageUp with the index finished: p50 {Percentile(finished, 0.5):F1} ms, p95 {Percentile(finished, 0.95):F1} ms, p99 {Percentile(finished, 0.99):F1} ms, max {finished[^1]:F1} ms");

        foreach (var (what, times) in new[] { ("PageDown/PageUp from the top of the file", scroll), ("wheel steps around the target of the jump, in front of the index", scrollAhead) })
        {
            times.Sort();
            if (times.Count == 0) { problems.Add($"no {what} was measured while the index ran"); continue; }
            double p50 = Percentile(times, 0.5), p95 = Percentile(times, 0.95), p99 = Percentile(times, 0.99);
            _output.WriteLine($"{times.Count} {what}: p50 {p50:F1} ms, p95 {p95:F1} ms, p99 {p99:F1} ms, max {times[^1]:F1} ms (limits p95 33, p99 80)");
            if (p95 > 33) problems.Add($"{what}: p95 {p95:F1} ms");
            if (p99 > 80) problems.Add($"{what}: p99 {p99:F1} ms");
        }
        _output.WriteLine($"the minimap started its picture {tab.Minimap?.BuildsStarted} times while the index ran");
        HugeText.Log("spans with the index finished (ms):\n" + string.Join('\n', TypingTrace.Summary().Split('\n').Take(10)));
        TypingTrace.Enabled = false;
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>Closing the tab of a file whose index is running stops the index quickly and lets go of the file.</summary>
    [AvaloniaFact]
    public void Closing_the_tab_while_a_50_GiB_file_is_indexed_ends_the_index_and_frees_the_file()
    {
        if (!HugeText.Enabled)
        {
            _output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        long size = HugeText.Size(50L << 30);
        string path = Files.Get("index.txt", size, null, _output);
        // the workers are slowed from the second GiB on, so the index is surely running when the tab is closed; they still check for a cancel after every block
        MappedFileSource.IndexBlockHook = (source, block) => { if (source.Path == path && block >= 1024) Thread.Sleep(300); };
        try
        {
            var tab = Open(path, out double callMs);
            var source = PagedOf(tab).Source;
            var hashing = tab.Doc.Doc.HashPass;
            Assert.False(tab.Paged!.Index.Complete);
            PumpUntil(() => PagedOf(tab).IndexedBytes >= 1L << 30, "a GiB of the index", TimeSpan.FromMinutes(5));
            using var self = Process.GetCurrentProcess();
            long setBefore = self.WorkingSet64;

            var clock = Stopwatch.StartNew();
            _w.CloseTab(tab);
            double closeMs = clock.Elapsed.TotalMilliseconds;
            Assert.True(source.IndexTask.IsCompleted, "the index task is still running after the tab was closed");
            Assert.True(hashing is not null && hashing.Wait(TimeSpan.FromSeconds(10)), "the pass that hashes the file in the background was not stopped by the close");

            FileStream? opened = null;
            clock.Restart();
            for (; opened is null && clock.Elapsed < TimeSpan.FromSeconds(10); Thread.Sleep(50))
            {
                Dispatcher.UIThread.RunJobs();
                try { opened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { }
            }
            double freeMs = clock.Elapsed.TotalMilliseconds;
            opened?.Dispose();
            GC.Collect();
            self.Refresh();
            _output.WriteLine($"OpenFile held the window for {callMs / 1000:F0} s; the tab closed in {closeMs:F0} ms with the index running; the file was free for the writer after {freeMs:F0} ms; working set {setBefore >> 20} MiB before, {self.WorkingSet64 >> 20} MiB after");
            Assert.True(closeMs < 2000, $"closing the tab took {closeMs:F0} ms");
            Assert.NotNull(opened);
        }
        finally { MappedFileSource.IndexBlockHook = null; }
    }
}

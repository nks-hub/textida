using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Tests;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace CrashHarness;

/// <summary>
/// P7.1.4 at the size of the target: a child process edits a document of tens of GiB with the journal on, and the parent kills it from outside, with no flush and at a moment the
/// child does not choose. The parent then recovers the journal and compares the recovered document, byte by byte, with a model that is computed from the same script of edits and
/// never from the product, checks that the file of the user is the original, whole, and measures how much work the kill took with it by the time stamps of the edits.
/// Three kills: <c>edit</c> among small edits, <c>paste</c> in the middle of a paste of a GiB that is made of 4 MiB inserts, <c>save</c> when the streamed save has written half of the file
/// while the edits go on.
/// </summary>
public static class HugeScenario
{
    public const int PasteChunk = 4 << 20;
    public const long LossLimitMs = 2000;
    public static readonly string[] Points = ["edit", "paste", "save", "cancel"];

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>The edits of the child, in order: small inserts, deletes and replacements spread over the document, and at <paramref name="pasteAt"/> a paste of <paramref name="pasteChunks"/> inserts of 4 MiB.</summary>
    public static List<LargeOp> Script(int count, int pasteAt, int pasteChunks)
    {
        var rng = new Random(91);
        var ops = new List<LargeOp>();
        for (int i = 0; i < count; i++)
        {
            if (i == pasteAt)
                for (int c = 0; c < pasteChunks; c++) ops.Add(new("paste", 500, 0, PasteChunk, 7000 + c));
            switch (i % 6)
            {
                case 0 or 1 or 2: ops.Add(new("insert", rng.Next(0, 1000), 0, rng.Next(1, 120), 5000 + i)); break;
                case 3: ops.Add(new("delete", rng.Next(0, 1000), rng.Next(1, 80), 0, 0)); break;
                case 4: ops.Add(new("replace", rng.Next(0, 1000), rng.Next(1, 40), rng.Next(1, 60), 6000 + i)); break;
                default: ops.Add(new("edge", rng.Next(2) * 999, 0, rng.Next(1, 30), 8000 + i)); break;
            }
        }
        return ops;
    }

    private sealed class Percent(StreamWriter stdout, CancellationTokenSource? cancelAtHalf) : IProgress<SaveProgress>
    {
        private int _last = -1;

        public void Report(SaveProgress progress)
        {
            int percent = (int)(progress.Written * 100 / Math.Max(1, progress.Total));
            if (percent == _last) return;
            _last = percent;
            lock (stdout) stdout.WriteLine($"save {percent} {Now()}");
            if (percent >= 50) cancelAtHalf?.Cancel();
        }
    }

    /// <summary>Opens the document with the journal, prints "ready", edits with a line "index time" after every edit, starts the save at <paramref name="saveAt"/> and waits to be killed.</summary>
    public static int Child(string dir, string point, int count, int pasteAt, int pasteChunks, int saveAt, int parentPid)
    {
        var watch = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(5000);
                try { Process.GetProcessById(parentPid).Dispose(); }
                catch (ArgumentException) { Environment.Exit(4); } // the parent is gone: nobody will kill this process
            }
        }) { IsBackground = true };
        watch.Start();

        Doc.SkipHashOnOpen = true; // see RunAll
        var factory = new PagedBackendFactory(startIndexing: false, spillBudgetBytes: PagedSpillPlaceholders.MemoryBudgetBytes, spillDirectory: Path.Combine(dir, "spill"));
        using var session = JournalSession.Start(Path.Combine(dir, "journal"), name => name == "paged" ? factory : null);
        using var doc = Doc.Open(Path.Combine(dir, "data", "doc.txt"), factory);
        session.Track(doc);
        session.Flush();
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        lock (stdout) stdout.WriteLine($"ready {Now()}");

        var ops = Script(count, pasteAt, pasteChunks);
        Task<SaveResult>? save = null;
        using var cancel = new CancellationTokenSource();
        for (int i = 0; i < ops.Count; i++)
        {
            if (point is "save" or "cancel" && i == saveAt)
            {
                doc.ProgressIntervalMs = 0; // a report after every MiB; the line is printed only when the whole percent changes
                save = doc.SaveAsync(cancellationToken: cancel.Token, progress: new Percent(stdout, point == "cancel" ? cancel : null));
            }
            var (offset, delete, insert) = ops[i].Resolve(doc.ByteLength);
            var bytes = new byte[insert];
            HugeText.Fill(ops[i].Seed, 0, bytes);
            if (!doc.Replace(new ByteRange(offset, offset + delete), bytes).Applied) throw new InvalidOperationException($"edit {i} was refused");
            lock (stdout) stdout.WriteLine($"{i} {Now()}");
            if (ops[i].Name != "paste") Thread.Sleep(point is "save" or "cancel" ? 50 : 10);
        }
        try { save?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (point == "cancel") { lock (stdout) stdout.WriteLine($"cancelled {Now()}"); }
        lock (stdout) stdout.WriteLine($"end {Now()}"); // the script ran out before the parent's trigger: the parent kills now
        Thread.Sleep(TimeSpan.FromMinutes(20));
        return 3;
    }

    /// <summary>Where the lines of the run go: the console, which the test shows when the run is over, and the file of <c>TEXTIDA_HUGE_LOG</c> at once, which stays when a run is killed.</summary>
    public sealed class Report
    {
        public void AppendLine(string line)
        {
            Console.WriteLine(line);
            HugeText.Log(line);
        }
    }

    private static ProcessStartInfo ChildStart(string dir, string point, int count, int pasteAt, int pasteChunks, int saveAt)
    {
        var self = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var a in new[] { "huge-child", dir, point, count.ToString(), pasteAt.ToString(), pasteChunks.ToString(), saveAt.ToString(), Environment.ProcessId.ToString() }) psi.ArgumentList.Add(a);
        return psi;
    }

    private static long FirstDifferenceInFile(string file, long size)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        if (fs.Length != size) return Math.Min(fs.Length, size);
        var actual = new byte[HugeText.BlockBytes];
        var expected = new byte[HugeText.BlockBytes];
        for (long at = 0; at < size;)
        {
            int n = (int)Math.Min(actual.Length, size - at);
            fs.ReadExactly(actual, 0, n);
            HugeText.Fill(0, at, expected.AsSpan(0, n));
            int same = actual.AsSpan(0, n).CommonPrefixLength(expected.AsSpan(0, n));
            if (same < n) return at + same;
            at += n;
        }
        return -1;
    }

    private static long FirstDifferenceInDocument(Doc doc, LargeModel model)
    {
        var actual = new byte[HugeText.BlockBytes];
        var expected = new byte[HugeText.BlockBytes];
        for (long at = 0; at < model.Length;)
        {
            int n = (int)Math.Min(actual.Length, model.Length - at);
            if (doc.Read(at, actual.AsSpan(0, n)) != n) return at;
            model.Read(at, expected.AsSpan(0, n));
            int same = actual.AsSpan(0, n).CommonPrefixLength(expected.AsSpan(0, n));
            if (same < n) return at + same;
            at += n;
        }
        return -1;
    }

    /// <summary>Runs one kill point on a fresh file; returns null when the recovery was whole and in time, else what was wrong. The numbers go to <paramref name="report"/>.</summary>
    public static string? RunPoint(string dir, string point, long size, int pasteChunks, Report report)
    {
        string data = Path.Combine(dir, "data"), journal = Path.Combine(dir, "journal"), spill = Path.Combine(dir, "spill");
        Directory.CreateDirectory(data);
        string file = Path.Combine(data, "doc.txt");
        var clock = Stopwatch.StartNew();
        HugeText.WriteFile(file, size);
        report.AppendLine($"[{point}] {size / 1073741824.0:F1} GiB written in {clock.Elapsed.TotalSeconds:F0} s");

        int small = Math.Max(2, pasteChunks / 8);
        (int count, int pasteAt, int chunks, int saveAt, int killOp) = point switch
        {
            "edit" => (600, -1, 0, -1, 300),
            "paste" => (600, 100, pasteChunks, -1, 100 + pasteChunks / 2),
            "cancel" => (1500, 10, small, 10 + small + 20, -1),
            _ => (3000, 10, small, 10 + small + 20, -1),
        };
        var ops = Script(count, pasteAt, chunks);
        var acks = new ConcurrentDictionary<int, long>();
        var events = new ConcurrentDictionary<string, long>();
        long readyAt = -1, killAt = 0;
        int savePercent = -1;
        bool cancelled = false;
        var trigger = new ManualResetEventSlim();

        using var proc = Process.Start(ChildStart(dir, point, count, pasteAt, chunks, saveAt))!;
        var errors = proc.StandardError.ReadToEndAsync();
        var reader = Task.Run(() =>
        {
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) is not null)
            {
                var parts = line.Split(' ');
                if (parts[0] == "ready") Volatile.Write(ref readyAt, long.Parse(parts[1]));
                else if (parts[0] == "cancelled") { Volatile.Write(ref cancelled, true); events["cancelled"] = long.Parse(parts[1]); }
                else if (parts[0] == "end") { events["end"] = long.Parse(parts[1]); trigger.Set(); }
                else if (parts[0] == "save")
                {
                    events.TryAdd("save " + (int.Parse(parts[1]) / 25 * 25), long.Parse(parts[2]));
                    Volatile.Write(ref savePercent, int.Parse(parts[1]));
                    if (point == "save" && savePercent >= 50) trigger.Set();
                }
                else if (parts.Length == 2 && int.TryParse(parts[0], out int op))
                {
                    acks[op] = long.Parse(parts[1]);
                    if (op == killOp) trigger.Set();
                }
            }
        });

        clock.Restart();
        try
        {
            while (Volatile.Read(ref readyAt) < 0 && !proc.HasExited && clock.Elapsed < TimeSpan.FromMinutes(20)) Thread.Sleep(50);
            if (Volatile.Read(ref readyAt) < 0) return "the child never became ready: " + errors.Result;
            report.AppendLine($"[{point}] the child opened the document and was ready after {clock.Elapsed.TotalSeconds:F0} s");
            clock.Restart();
            while (!trigger.Wait(500))
            {
                if (proc.HasExited) return "the child ended by itself: " + errors.Result;
                if (clock.Elapsed > TimeSpan.FromMinutes(25)) return "the kill trigger never came";
            }
            Thread.Sleep(new Random(point.Length * 31).Next(0, 250)); // the kill lands somewhere in the edit after the trigger
            killAt = Now();
            if (!proc.HasExited) proc.Kill(true);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            proc.WaitForExit();
            reader.Wait();
        }

        int acked = acks.Count == 0 ? 0 : acks.Keys.Max() + 1; // edits that returned in the child before the kill
        report.AppendLine($"[{point}] killed {killAt - readyAt} ms after ready, {acked} of {ops.Count} edits had returned{(point is "save" or "cancel" ? $", the save had written {savePercent} %" : "")}{(cancelled ? ", the save was cancelled" : "")}");
        if (events.Count > 0) report.AppendLine($"[{point}] timeline after ready, ms: " + string.Join(", ", events.OrderBy(e => e.Value).Select(e => $"{e.Key} at {e.Value - readyAt}")));
        if (saveAt >= 0 && acks.ContainsKey(saveAt) && acks.ContainsKey(saveAt - 1))
        {
            long Median(IEnumerable<long> gaps) { var list = gaps.Order().ToList(); return list.Count == 0 ? 0 : list[list.Count / 2]; }
            var gapsBefore = Enumerable.Range(1, saveAt - 1).Where(i => acks.ContainsKey(i) && acks.ContainsKey(i - 1)).Select(i => acks[i] - acks[i - 1]);
            var gapsAfter = Enumerable.Range(saveAt + 1, Math.Max(0, acked - saveAt - 1)).Where(i => acks.ContainsKey(i) && acks.ContainsKey(i - 1)).Select(i => acks[i] - acks[i - 1]);
            report.AppendLine($"[{point}] time between two edits: median {Median(gapsBefore)} ms before the save started, {Median(gapsAfter)} ms after (max {gapsAfter.DefaultIfEmpty().Max()} ms)");
        }
        if (point == "cancel" && !cancelled) return "the save was not cancelled";

        // the states of the model: after k edits, and its lengths
        var lengths = new long[ops.Count + 1];
        var walker = new LargeModel(size, HugeText.Fill);
        lengths[0] = size;
        for (int k = 0; k < ops.Count; k++)
        {
            var (offset, delete, insert) = ops[k].Resolve(walker.Length);
            walker.Replace(offset, delete, ops[k].Seed, insert);
            lengths[k + 1] = walker.Length;
        }
        LargeModel ModelAfter(int k)
        {
            var m = new LargeModel(size, HugeText.Fill);
            for (int i = 0; i < k; i++)
            {
                var (offset, delete, insert) = ops[i].Resolve(m.Length);
                m.Replace(offset, delete, ops[i].Seed, insert);
            }
            return m;
        }

        string journalPath = Directory.GetFiles(journal, "session-*.njl").Single();
        var factory = new PagedBackendFactory(startIndexing: false, spillBudgetBytes: PagedSpillPlaceholders.MemoryBudgetBytes, spillDirectory: spill);
        clock.Restart();
        var recovery = JournalRecovery.Recover(journalPath, name => name == "paged" ? factory : null);
        double recoverSeconds = clock.Elapsed.TotalSeconds;
        try
        {
            if (recovery.Documents.Count != 1) return $"{recovery.Documents.Count} documents were recovered: {string.Join("; ", recovery.Notes)}";
            var r = recovery.Documents[0];
            if (r.Status is not (RecoveryStatus.Restored or RecoveryStatus.Clean)) return $"recovery {r.Status}: {r.Error}";
            var doc = r.Document!;
            report.AppendLine($"[{point}] the journal was read and the document restored in {recoverSeconds:F0} s, {doc.ByteLength} bytes");

            // which state the journal holds: the newest one of the right length that the bytes confirm
            var verify = Stopwatch.StartNew();
            int found = -1;
            for (int k = Math.Min(ops.Count, acked + 1); k >= Math.Max(0, acked - 600) && found < 0; k--)
            {
                if (lengths[k] != doc.ByteLength) continue;
                var model = ModelAfter(k);
                var inFile = Task.Run(() => FirstDifferenceInFile(file, size));
                long wrong = FirstDifferenceInDocument(doc, model);
                long fileWrong = inFile.Result;
                if (fileWrong >= 0) return $"the file of the user differs from the original at byte {fileWrong}";
                if (wrong < 0) found = k;
                else report.AppendLine($"[{point}] the state after {k} edits has the right length but differs at byte {wrong}");
            }
            if (found < 0) return $"the recovered document ({doc.ByteLength} bytes) is none of the states after {Math.Max(0, acked - 600)} to {Math.Min(ops.Count, acked + 1)} edits";
            report.AppendLine($"[{point}] the document equals the state after {found} edits byte by byte, and the file is the original, whole; both compared in {verify.Elapsed.TotalSeconds:F0} s");

            long TimeOfState(int k) => k == 0 ? Volatile.Read(ref readyAt) : acks.GetValueOrDefault(k - 1, killAt);
            long loss = found < acked ? TimeOfState(acked) - TimeOfState(found) : 0;
            long lag = recovery.LastRecord is { } last && acked > 0 ? Math.Max(0, TimeOfState(acked) - last.ToUnixTimeMilliseconds()) : 0;
            report.AppendLine($"[{point}] work lost: {(found < acked ? $"{acked - found} edits, {loss} ms" : "none")} (limit {LossLimitMs} ms); the last journal record is {lag} ms older than the last edit that returned");
            if (loss > LossLimitMs) return $"{loss} ms of work were lost (limit {LossLimitMs} ms)";

            var leftovers = Directory.GetFiles(data).Where(f => f != file).Select(Path.GetFileName).ToList();
            if (leftovers.Count > 0) return "files left next to the document: " + string.Join(", ", leftovers);
            if (Directory.Exists(spill) && Directory.GetFiles(spill, $"{proc.Id}-*.spill").Length > 0) return "a spill file of the killed process was not removed";
            return null;
        }
        finally
        {
            foreach (var d in recovery.Documents) d.Document?.Dispose();
        }
    }

    public static int RunAll(string work, long size, int pasteChunks, string[]? points)
    {
        // Opening a document, the child's and the one that recovery rebuilds, reads and hashes the whole file; for 50 GiB that is a measurement of its own (see HugeCalibrationTests) and
        // longer than the 20 minutes a test gets. What is measured here is what comes after it, so the read is switched off on this thread, the thread that recovers.
        Doc.SkipHashOnOpen = true;
        Directory.CreateDirectory(work);
        var failures = new List<string>();
        foreach (var point in points ?? Points)
        {
            string dir = Path.Combine(work, point);
            var report = new Report();
            string? problem;
            try
            {
                HugeText.RequireRoom(work, size * 17 / 10 + (4L << 30));
                problem = RunPoint(dir, point, size, pasteChunks, report);
            }
            catch (Exception e) { problem = "harness error: " + e; }
            report.AppendLine($"[{point}] {(problem is null ? "ok" : "FAIL " + problem)}");
            if (problem is not null) failures.Add($"{point}: {problem}");
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
        Console.WriteLine($"failures {failures.Count}");
        foreach (var f in failures) Console.WriteLine("FAILED " + f);
        return failures.Count == 0 ? 0 : 1;
    }
}

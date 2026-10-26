using System.Diagnostics;
using System.Globalization;
using System.Text;
using AvaloniaEdit.Document;
using NksPad.Core;

namespace NksPad.Bench;

/// <summary>
/// Spike: editing a 100 MiB text through the paged core (piece tree with line counts) against AvaloniaEdit
/// <see cref="TextDocument"/>, same edits, no UI and no rendering. One process per engine and file:
/// <c>e100 &lt;file&gt; --engine paged|avalonia|avalonia-chunked [--reps N]</c>.
/// </summary>
internal static class E100Edit
{
    private const int ViewLines = 60;

    /// <summary>Caret resolved to the start of its line; <see cref="Start"/> is in engine units (bytes or chars).</summary>
    private readonly record struct Caret(long Start, string Text);

    /// <summary>What a view needs from a document. Positions are line + UTF-16 column, so both engines run the same edits.</summary>
    private interface ITarget
    {
        long LineCount { get; }
        long LineStart(long line);
        Caret Resolve(long line);
        IReadOnlyList<string> View(long top);
        void Insert(Caret c, int col, string text);
        void Delete(Caret c, int col, int chars);
        bool Undo();
        bool Redo();
        /// <summary>Line starts from a full scan of the current text, independent of the engine's line index.</summary>
        List<long> Rescan();
        void Save(string path);
        void Close();
    }

    private sealed class PagedTarget(PagedDocument doc) : ITarget
    {
        public long LineCount => doc.LineIndexState.Lines;
        public long LineStart(long line) => doc.TryGetLineStart(line, out long o) ? o : throw new InvalidOperationException($"line {line} has no start");
        public Caret Resolve(long line) { long s = LineStart(line); return new(s, doc.ReadLines(s, 1)[0]); }
        public IReadOnlyList<string> View(long top) => doc.ReadLines(LineStart(top), ViewLines);
        public void Insert(Caret c, int col, string text) => doc.Insert(c.Start + doc.Encoding.GetByteCount(c.Text.AsSpan(0, col)), text);
        public void Delete(Caret c, int col, int chars) =>
            doc.Delete(c.Start + doc.Encoding.GetByteCount(c.Text.AsSpan(0, col)), doc.Encoding.GetByteCount(c.Text.AsSpan(col, chars)));
        public bool Undo() => doc.Undo();
        public bool Redo() => doc.Redo();
        public void Save(string path) => doc.SaveAsync(path).GetAwaiter().GetResult();
        public void Close() => doc.Dispose();

        public List<long> Rescan()
        {
            var b = new byte[doc.ByteLength];
            if (doc.Read(0, b) != b.Length) throw new InvalidOperationException("short read");
            var starts = new List<long> { 0 };
            for (int i = 0; i < b.Length; i++)
                if (b[i] == '\n' || (b[i] == '\r' && (i + 1 == b.Length || b[i + 1] != '\n'))) starts.Add(i + 1);
            return starts;
        }
    }

    private sealed class AvaloniaTarget(TextDocument doc) : ITarget
    {
        public long LineCount => doc.LineCount;
        public long LineStart(long line) => doc.GetLineByNumber((int)line + 1).Offset;
        public Caret Resolve(long line) { var l = doc.GetLineByNumber((int)line + 1); return new(l.Offset, doc.GetText(l)); }
        public void Insert(Caret c, int col, string text) => doc.Insert((int)c.Start + col, text);
        public void Delete(Caret c, int col, int chars) => doc.Remove((int)c.Start + col, chars);
        public bool Undo() { if (!doc.UndoStack.CanUndo) return false; doc.UndoStack.Undo(); return true; }
        public bool Redo() { if (!doc.UndoStack.CanRedo) return false; doc.UndoStack.Redo(); return true; }
        public void Close() { }

        public IReadOnlyList<string> View(long top)
        {
            var lines = new List<string>(ViewLines);
            for (long i = top; i < Math.Min(top + ViewLines, doc.LineCount); i++) lines.Add(doc.GetText(doc.GetLineByNumber((int)i + 1)));
            return lines;
        }

        public void Save(string path)
        {
            using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
            doc.WriteTextTo(w);
        }

        public List<long> Rescan()
        {
            string t = doc.Text;
            var starts = new List<long> { 0 };
            for (int i = 0; i < t.Length; i++)
                if (t[i] == '\n' || (t[i] == '\r' && (i + 1 == t.Length || t[i + 1] != '\n'))) starts.Add(i + 1);
            return starts;
        }
    }

    private static ITarget Open(string engine, string path) => engine switch
    {
        "paged" => OpenPaged(path),
        "avalonia" => new AvaloniaTarget(new TextDocument(File.ReadAllText(path))),
        "avalonia-chunked" => new AvaloniaTarget(LoadChunked(path)),
        _ => throw new ArgumentException("engine: paged | avalonia | avalonia-chunked"),
    };

    private static PagedTarget OpenPaged(string path)
    {
        var doc = PagedDocument.Open(path);
        doc.Source.IndexTask.Wait();
        return new PagedTarget(doc);
    }

    private static TextDocument LoadChunked(string path)
    {
        var doc = new TextDocument();
        doc.UndoStack.SizeLimit = 0;
        using var sr = new StreamReader(path, Encoding.UTF8, true, 1 << 20);
        var buf = new char[1 << 20];
        int n;
        while ((n = sr.Read(buf, 0, buf.Length)) > 0) doc.Insert(doc.TextLength, new string(buf, 0, n));
        doc.UndoStack.SizeLimit = int.MaxValue;
        return doc;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        Console.WriteLine("CHECK FAILED: " + what);
        Environment.ExitCode = 1;
        throw new InvalidOperationException(what);
    }

    private sealed class Recorder(Csv? csv, long baseline)
    {
        public bool Quiet => csv is null;

        public void Rec(string metric, int rep, double v, string unit) => csv?.Row("E100", metric, rep, v, unit);

        public void Report(string metric, List<double> v, string unit)
        {
            if (csv is null) return;
            for (int i = 0; i < v.Count; i++) Rec(metric, i, v[i], unit);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{metric}: n={v.Count} p50={Stats.Percentile(v, .5):F4}{unit} p95={Stats.Percentile(v, .95):F4}{unit} p99={Stats.Percentile(v, .99):F4}{unit} max={v.Max():F4}{unit}"));
        }

        public void Mem(string label)
        {
            if (csv is null) return;
            var m = NksPad.Bench.Mem.Snapshot();
            Console.WriteLine($"mem[{label}]: private +{NksPad.Bench.Mem.Mib(m.PrivateBytes - baseline)} MiB (abs {NksPad.Bench.Mem.Mib(m.PrivateBytes)}), workingSet={NksPad.Bench.Mem.Mib(m.WorkingSet)} MiB, peakCommit={NksPad.Bench.Mem.Mib(m.PeakCommit)} MiB");
            Rec($"private-delta:{label}", 0, m.PrivateBytes - baseline, "B");
            Rec($"workingset:{label}", 0, m.WorkingSet, "B");
            Rec($"peakcommit:{label}", 0, m.PeakCommit, "B");
        }
    }

    public static void Run(string[] args)
    {
        string name = args[1];
        string engine = Args.Opt(args, "--engine") ?? "paged";
        int reps = int.Parse(Args.Opt(args, "--reps") ?? "30");
        string path = Path.Combine(Paths.Corpus, name);
        Directory.CreateDirectory(Paths.Scratch);
        Console.WriteLine($"== E100 {name} engine={engine}: {new FileInfo(path).Length} B, reps={reps}, cpu={Environment.ProcessorCount}, AvaloniaEdit {typeof(TextDocument).Assembly.GetName().Version}");
        if (Args.Flag(args, "--cold"))
        {
            // one sample per process, no warm-up: the caller purges the file cache first; includes JIT
            long c0 = Stopwatch.GetTimestamp();
            var ct = FirstEdit(engine, path, out double coldView);
            double coldMs = Ms(Stopwatch.GetTimestamp() - c0);
            ct.Close();
            using var coldCsv = new Csv($"e100-{engine}-{name}-cold.csv", "scenario,metric,rep,value,unit");
            coldCsv.Row("E100", "first-view-cold", 0, coldView, "ms");
            coldCsv.Row("E100", "first-edit-cold", 0, coldMs, "ms");
            Console.WriteLine($"cold: first view {coldView:F1} ms, first edit visible {coldMs:F1} ms");
            return;
        }

        // JIT warm-up on a 1 MiB prefix, so the baseline is a stabilized process and not a cold one
        string warm = Path.Combine(Paths.Scratch, $"e100-warm-{engine}-{name}");
        var head = new byte[1 << 20];
        using (var fs = File.OpenRead(path)) fs.ReadExactly(head);
        File.WriteAllBytes(warm, head.AsSpan(0, Array.LastIndexOf(head, (byte)'\n') + 1));
        Session(engine, warm, name, new Recorder(null, 0));
        File.Delete(warm);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        long baseline = Mem.Snapshot().PrivateBytes;
        Console.WriteLine($"baseline private {Mem.Mib(baseline)} MiB (after warm-up and GC)");
        using var csv = new Csv($"e100-{engine}-{name}.csv", "scenario,metric,rep,value,unit");
        var rec = new Recorder(csv, baseline);
        Session(engine, path, name, rec);

        var first = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            long t0 = Stopwatch.GetTimestamp();
            var t = FirstEdit(engine, path, out _);
            first.Add(Ms(Stopwatch.GetTimestamp() - t0));
            t.Close();
        }
        rec.Report("first-edit-warm", first, "ms");
        Console.WriteLine($"final peak commit {Mem.Mib(Mem.Snapshot().PeakCommit)} MiB");
    }

    /// <summary>Open, first viewport, one inserted character on line 30 and the viewport again.</summary>
    private static ITarget FirstEdit(string engine, string path, out double viewMs)
    {
        long t0 = Stopwatch.GetTimestamp();
        var t = Open(engine, path);
        var v0 = t.View(0);
        viewMs = Ms(Stopwatch.GetTimestamp() - t0);
        var c = t.Resolve(30);
        t.Insert(c, 5, "#");
        var v1 = t.View(0);
        Check(v0.Count == ViewLines && v1[30] == v0[30].Insert(5, "#"), "first edit visible in viewport");
        return t;
    }

    private static void Session(string engine, string path, string name, Recorder rec)
    {
        bool czech = !name.StartsWith("ascii");
        const string czechChars = "příliš žluťoučký kůň úpěl ďábelské ódy";
        string Char(int i) => czech ? czechChars[i % czechChars.Length].ToString() : ((char)('a' + i % 26)).ToString();

        long t0 = Stopwatch.GetTimestamp();
        var t = FirstEdit(engine, path, out double viewMs);
        double firstMs = Ms(Stopwatch.GetTimestamp() - t0);
        rec.Rec("first-view", 0, viewMs, "ms");
        rec.Rec("first-edit", 0, firstMs, "ms");
        if (!rec.Quiet) Console.WriteLine($"first view {viewMs:F1} ms, first edit visible {firstMs:F1} ms, lines={t.LineCount}");
        rec.Mem("first-edit");

        var total = new List<double>(); var edit = new List<double>(); var view = new List<double>();
        void Op(long line, int col, string? insert, long top)
        {
            long a = Stopwatch.GetTimestamp();
            var c = t.Resolve(line);
            long b = Stopwatch.GetTimestamp();
            if (insert is not null) t.Insert(c, col, insert); else t.Delete(c, col, 1);
            long e = Stopwatch.GetTimestamp();
            _ = t.LineCount;
            var v = t.View(top);
            long f = Stopwatch.GetTimestamp();
            total.Add(Ms(f - a)); edit.Add(Ms(e - b)); view.Add(Ms(f - e));
            string expect = insert is not null ? c.Text.Insert(col, insert) : c.Text.Remove(col, 1);
            Check(v[(int)(line - top)] == expect, $"viewport after edit at line {line}");
        }

        // random places: 2 000 inserts and 500 deletes interleaved (every fifth edit deletes)
        var r = new Random(11);
        for (int i = 0; i < 2500; i++)
        {
            long line = (long)(r.NextDouble() * (t.LineCount - 1));
            double colFrac = r.NextDouble();
            int len = t.Resolve(line).Text.Length;
            Check(len > 0, "non-empty line");
            bool del = i % 5 == 4;
            Op(line, del ? (int)(colFrac * len) : (int)(colFrac * (len + 1)), del ? null : Char(i), Math.Max(0, line - 30));
        }
        rec.Report("random:total", total, "ms"); rec.Report("random:edit", edit, "ms"); rec.Report("random:view", view, "ms");
        total.Clear(); edit.Clear(); view.Clear();

        // one place: typing a word, four characters then one backspace, 2 000 inserts and 500 deletes
        long fixedLine = t.LineCount / 2, fixedTop = fixedLine - 30;
        int caret = 10;
        string afterFirst2000 = "";
        for (int i = 0; i < 500; i++)
        {
            for (int k = 0; k < 4; k++) Op(fixedLine, caret++, Char(i * 4 + k), fixedTop);
            Op(fixedLine, --caret, null, fixedTop);
            if (i == 399) afterFirst2000 = t.Resolve(fixedLine).Text;
        }
        rec.Report("fixed:total", total, "ms"); rec.Report("fixed:edit", edit, "ms"); rec.Report("fixed:view", view, "ms");
        rec.Mem("after-typing");

        // undo and redo the last 500 edits, each followed by a viewport refresh
        string beforeUndo = t.Resolve(fixedLine).Text;
        long linesBeforeUndo = t.LineCount;
        var undo = new List<double>(); var redo = new List<double>();
        for (int i = 0; i < 500; i++)
        {
            long a = Stopwatch.GetTimestamp();
            Check(t.Undo(), "undo available");
            t.View(fixedTop);
            undo.Add(Ms(Stopwatch.GetTimestamp() - a));
        }
        Check(t.Resolve(fixedLine).Text == afterFirst2000, "undo of 500 edits restores the line");
        for (int i = 0; i < 500; i++)
        {
            long a = Stopwatch.GetTimestamp();
            Check(t.Redo(), "redo available");
            t.View(fixedTop);
            redo.Add(Ms(Stopwatch.GetTimestamp() - a));
        }
        Check(t.Resolve(fixedLine).Text == beforeUndo && t.LineCount == linesBeforeUndo, "redo restores the line");
        rec.Report("undo+view", undo, "ms"); rec.Report("redo+view", redo, "ms");
        rec.Mem("after-undo-redo");

        // go to line N and show 60 lines
        var go = new List<double>();
        var g = new Random(13);
        long lineCount = t.LineCount;
        for (int i = 0; i < 1000; i++)
        {
            long n = g.NextInt64(lineCount);
            long a = Stopwatch.GetTimestamp();
            var v = t.View(n);
            go.Add(Ms(Stopwatch.GetTimestamp() - a));
            Check(v.Count > 0, "goto viewport");
        }
        rec.Report("goto+view", go, "ms");
        rec.Mem("after-goto");

        string outPath = Path.Combine(Paths.Scratch, $"e100-out-{engine}-{Path.GetFileName(path)}");
        var swSave = Stopwatch.StartNew();
        t.Save(outPath);
        double saveSec = swSave.Elapsed.TotalSeconds;
        rec.Rec("save", 0, saveSec * 1000, "ms");
        rec.Mem("after-save");

        // every 7th line plus the last one: each lookup scans up to one index block, all 800 000 would take minutes on the VM
        var swScan = Stopwatch.StartNew();
        var starts = t.Rescan();
        Check(starts.Count == lineCount, $"line count {lineCount} equals rescan {starts.Count}");
        var sample = Enumerable.Range(0, (starts.Count + 6) / 7).Select(i => i * 7L).Append(starts.Count - 1).ToList();
        foreach (long n in sample)
            Check(t.LineStart(n) == starts[(int)n], $"line {n} start {t.LineStart(n)} equals rescan {starts[(int)n]}");
        int checkedLines = sample.Count;
        if (!rec.Quiet) Console.WriteLine($"line index after 5000 edits + 500 undo + 500 redo: {lineCount} lines equal full rescan, {checkedLines} line starts equal ({swScan.Elapsed.TotalSeconds:F1}s)");
        t.Close();
        string sha = Hashing.Sha256File(outPath);
        long outLen = new FileInfo(outPath).Length;
        File.Delete(outPath);
        if (rec.Quiet) return;

        Console.WriteLine($"save {saveSec * 1000:F0} ms, {outLen} B (original {new FileInfo(path).Length} B), sha256={sha}");
        Directory.CreateDirectory(Paths.Results);
        File.WriteAllText(Path.Combine(Paths.Results, $"e100-{engine}-{name}.sha"), sha + "\n");
        foreach (var other in Directory.GetFiles(Paths.Results, $"e100-*-{name}.sha"))
        {
            string otherEngine = Path.GetFileName(other)["e100-".Length..^($"-{name}.sha".Length)];
            if (otherEngine == engine) continue;
            bool same = File.ReadAllText(other).Trim() == sha;
            Console.WriteLine($"saved bytes vs {otherEngine}: {(same ? "OK sha256 identical" : "MISMATCH")}");
            if (!same) Environment.ExitCode = 1;
        }
    }
}

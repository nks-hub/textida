using System.Diagnostics;
using AvaloniaEdit.Document;

namespace NksHub.Textida.Bench;

/// <summary>Scenario A2: AvaloniaEdit <see cref="TextDocument"/> without UI. Run one process per file.</summary>
internal static class A2AvaloniaEdit
{
    private static double Us(long t0) => (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;

    public static void Run(string[] args)
    {
        string name = args[1];
        string path = Path.Combine(Paths.Corpus, name);
        bool probe = Args.Flag(args, "--probe");
        using var csv = new Csv($"a2-{name}{(Args.Flag(args, "--chunked") ? "-chunked" : probe ? "-probe" : "")}.csv", "scenario,metric,rep,value,unit");
        void Report(string metric, List<double> v, string unit)
        {
            for (int i = 0; i < v.Count; i++) csv.Row("A2", metric, i, v[i], unit);
            Console.WriteLine(Stats.Summary(metric, v, unit));
        }

        Console.WriteLine($"== A2 {name}: {new FileInfo(path).Length} B, probe={probe}, version {typeof(TextDocument).Assembly.GetName().Version}");
        if (probe) { Probe(path); return; }

        TextDocument? doc = null;
        foreach (var method in Args.Flag(args, "--chunked") ? new[] { "chunked-insert" } : new[] { "ReadAllText+ctor", "chunked-insert" })
        {
            doc = null;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            long managedBefore = GC.GetTotalMemory(true);
            long privBefore = Mem.Snapshot().PrivateBytes;
            var sw = Stopwatch.StartNew();
            try
            {
                doc = method == "ReadAllText+ctor" ? LoadAllText(path) : LoadChunked(path, out _, out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{method}: FAILED after {sw.Elapsed.TotalSeconds:F2}s: {ex.GetType().Name}: {ex.Message}");
                csv.Row("A2", $"load:{method}", 0, "FAILED:" + ex.GetType().Name, "");
                doc = null;
                continue;
            }
            double sec = sw.Elapsed.TotalSeconds;
            var peak = Mem.Snapshot();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            long managedAfter = GC.GetTotalMemory(true);
            var after = Mem.Snapshot();
            Console.WriteLine($"{method}: {sec:F2}s, chars={doc.TextLength}, lines={doc.LineCount}, managed live +{Mem.Mib(managedAfter - managedBefore)} MiB, private now +{Mem.Mib(after.PrivateBytes - privBefore)} MiB, peak commit {Mem.Mib(peak.PeakCommit)} MiB (process peak, start {Mem.Mib(privBefore)} MiB private)");
            csv.Row("A2", $"load-time:{method}", 0, sec, "s");
            csv.Row("A2", $"managed-delta:{method}", 0, managedAfter - managedBefore, "B");
            csv.Row("A2", $"private-delta:{method}", 0, after.PrivateBytes - privBefore, "B");
            csv.Row("A2", $"peak-commit:{method}", 0, peak.PeakCommit, "B");
            break;
        }
        if (doc is null)
        {
            Console.WriteLine("no document could be loaded, editing scenarios skipped");
            return;
        }

        doc.UndoStack.ClearAll();
        var rnd = new Random(3);
        var ins = new List<double>(); var del = new List<double>(); var undo = new List<double>(); var line = new List<double>();
        for (int i = 0; i < 1000; i++)
        {
            int off = doc.TextLength / 2 + rnd.Next(-100000, 100000);
            long t0 = Stopwatch.GetTimestamp();
            doc.Insert(off, "x");
            ins.Add(Us(t0));
        }
        for (int i = 0; i < 1000; i++)
        {
            int off = doc.TextLength / 2 + rnd.Next(-100000, 100000);
            long t0 = Stopwatch.GetTimestamp();
            doc.Remove(off, 1);
            del.Add(Us(t0));
        }
        for (int i = 0; i < 2000 && doc.UndoStack.CanUndo; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            doc.UndoStack.Undo();
            undo.Add(Us(t0));
        }
        int lines = doc.LineCount;
        for (int i = 0; i < 100_000; i++)
        {
            int n = rnd.Next(1, lines + 1);
            long t0 = Stopwatch.GetTimestamp();
            _ = doc.GetLineByNumber(n);
            line.Add(Us(t0));
        }
        Report("insert-1-char-middle", ins, "us");
        Report("remove-1-char-middle", del, "us");
        Report("undo", undo, "us");
        Report("GetLineByNumber-random", line, "us");
        Console.WriteLine($"final text length {doc.TextLength}, undo remaining {doc.UndoStack.CanUndo}");
    }

    private static TextDocument LoadAllText(string path) => new(File.ReadAllText(path));

    private static TextDocument LoadChunked(string path, out long chars, out Exception? failure)
    {
        var doc = new TextDocument();
        doc.UndoStack.SizeLimit = 0;
        chars = 0;
        failure = null;
        using var sr = new StreamReader(path, System.Text.Encoding.UTF8, true, 1 << 20);
        var buf = new char[1 << 20];
        int n;
        while ((n = sr.Read(buf, 0, buf.Length)) > 0)
        {
            doc.Insert(doc.TextLength, new string(buf, 0, n));
            chars += n;
        }
        doc.UndoStack.SizeLimit = int.MaxValue;
        return doc;
    }

    /// <summary>Loads chunk by chunk until the document fails, to find the real limit.</summary>
    private static void Probe(string path)
    {
        var sw = Stopwatch.StartNew();
        var doc = new TextDocument();
        doc.UndoStack.SizeLimit = 0;
        long chars = 0;
        try
        {
            using var sr = new StreamReader(path, System.Text.Encoding.UTF8, true, 1 << 20);
            var buf = new char[1 << 20];
            int n;
            while ((n = sr.Read(buf, 0, buf.Length)) > 0)
            {
                doc.Insert(doc.TextLength, new string(buf, 0, n));
                chars += n;
                if ((chars & ((1 << 28) - 1)) < 1 << 20) Console.WriteLine($"  {chars / 1_000_000} M chars, {sw.Elapsed.TotalSeconds:F1}s, private {Mem.Mib(Mem.Snapshot().PrivateBytes)} MiB");
            }
            Console.WriteLine($"probe: whole file loaded, chars={chars}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"probe: FAILED at {chars} chars read (doc.TextLength={doc.TextLength}) after {sw.Elapsed.TotalSeconds:F1}s: {ex.GetType().Name}: {ex.Message}");
        }
        var m = Mem.Snapshot();
        Console.WriteLine($"probe: private {Mem.Mib(m.PrivateBytes)} MiB, peak commit {Mem.Mib(m.PeakCommit)} MiB");
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core;

namespace NksHub.Textida.Bench;

/// <summary>Scenario A3: paged backend on one corpus file. Run one process per file so memory numbers are isolated.</summary>
internal static class A3Paged
{
    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static long ExpectedLfCount(string name)
    {
        string line = File.ReadAllText(Path.Combine(Paths.Manifest, name + ".txt")).Trim();
        return long.Parse(line.Split("lines_lf=")[1]);
    }

    public static string ExpectedSha(string name) => File.ReadAllText(Path.Combine(Paths.Manifest, name + ".txt")).Trim().Split(',')[2];

    public static async Task Run(string[] args)
    {
        string name = args[1];
        string path = Path.Combine(Paths.Corpus, name);
        int reps = int.Parse(Args.Opt(args, "--reps") ?? "3");
        int samples = int.Parse(Args.Opt(args, "--samples") ?? "30");
        bool inPlace = Args.Flag(args, "--inplace");
        long length = new FileInfo(path).Length;
        long expectedLines = ExpectedLfCount(name) + 1;
        using var csv = new Csv($"a3-{name}.csv", "scenario,metric,rep,value,unit");
        void Rec(string metric, int rep, double v, string unit)
        {
            csv.Row("A3", metric, rep, v, unit);
        }
        void Report(string metric, List<double> v, string unit)
        {
            for (int i = 0; i < v.Count; i++) Rec(metric, i, v[i], unit);
            Console.WriteLine(Stats.Summary(metric, v, unit));
        }
        void Mem(string label)
        {
            var m = NksHub.Textida.Bench.Mem.Snapshot();
            Console.WriteLine($"mem[{label}]: private={NksHub.Textida.Bench.Mem.Mib(m.PrivateBytes)} MiB, workingSet={NksHub.Textida.Bench.Mem.Mib(m.WorkingSet)} MiB, peakWS={NksHub.Textida.Bench.Mem.Mib(m.PeakWorkingSet)} MiB, peakCommit={NksHub.Textida.Bench.Mem.Mib(m.PeakCommit)} MiB");
            Rec($"private:{label}", 0, m.PrivateBytes, "B");
            Rec($"workingset:{label}", 0, m.WorkingSet, "B");
            Rec($"peakworkingset:{label}", 0, m.PeakWorkingSet, "B");
            Rec($"peakcommit:{label}", 0, m.PeakCommit, "B");
        }

        Console.WriteLine($"== A3 {name}: {length} B ({length / 1073741824.0:F2} GiB), reps={reps}, samples={samples}, cpu={Environment.ProcessorCount}");
        Mem("process-start");

        // 1. first image: open + first 100 lines. Sample 0 is the first call in the process (includes JIT).
        var first = new List<double>();
        var sw = Stopwatch.StartNew();
        using (var doc = PagedDocument.Open(path))
        {
            var lines = await doc.ReadLinesAtAsync(0, 100);
            first.Add(sw.Elapsed.TotalMilliseconds);
            Console.WriteLine($"first call (cold JIT, indexer running): {first[0]:F1} ms, lines={lines.Count}, firstLine='{lines[0][..Math.Min(40, lines[0].Length)]}'");
            Mem("after-first-image");
        }
        var warmNoIndex = new List<double>();
        var warmIndexing = new List<double>();
        for (int i = 0; i < samples; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            using (var d = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
                await d.ReadLinesAtAsync(0, 100);
            warmNoIndex.Add(Ms(Stopwatch.GetTimestamp() - t0));
            t0 = Stopwatch.GetTimestamp();
            using (var d = PagedDocument.Open(path))
            {
                await d.ReadLinesAtAsync(0, 100);
                warmIndexing.Add(Ms(Stopwatch.GetTimestamp() - t0));
            }
        }
        Report("first-image-no-index", warmNoIndex, "ms");
        Report("first-image-indexer-running", warmIndexing, "ms");

        // 2. jumps by byte offset, always to fresh pages
        using (var src = MappedFileSource.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            var mid = new List<double>();
            var end = new List<double>();
            var rnd = new Random(42);
            for (int i = 0; i < samples; i++)
            {
                long off = length / 2 + rnd.NextInt64(-length / 20, length / 20);
                long t0 = Stopwatch.GetTimestamp();
                long start = src.AlignToLineStart(off);
                var l = await src.ReadLinesAtAsync(start, 50);
                mid.Add(Ms(Stopwatch.GetTimestamp() - t0));
                if (l.Count == 0) throw new InvalidOperationException("empty page");

                off = length - 1 - rnd.NextInt64(0, 1 << 20);
                t0 = Stopwatch.GetTimestamp();
                start = src.AlignToLineStart(off);
                l = await src.ReadLinesAtAsync(start, 50);
                end.Add(Ms(Stopwatch.GetTimestamp() - t0));
            }
            Report("jump-50pct", mid, "ms");
            Report("jump-end", end, "ms");
        }
        if (reps == 0) return; // --reps 0: first image and jumps only, for runs after a cache purge

        // 3. full index
        var indexSec = new List<double>();
        var gbs = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            using var src = MappedFileSource.Open(path);
            await src.IndexTask;
            double sec = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            indexSec.Add(sec);
            gbs.Add(length / sec / 1e9);
            var (lines, complete) = src.LineIndexState;
            Console.WriteLine($"index rep {i}: {sec:F2}s, lines={lines} (expected {expectedLines}) {(lines == expectedLines && complete ? "OK" : "MISMATCH")}");
            if (lines != expectedLines || !complete) Environment.ExitCode = 1;
            if (i == reps - 1)
            {
                Mem("after-index");
                bool ok = src.TryGetLineStart(lines - 1, out long lastStart);
                Console.WriteLine($"last line start = {lastStart} ({ok}); TryGetLineStart(N/2) ok={src.TryGetLineStart(lines / 2, out long midStart)} -> {midStart}");
            }
        }
        Report("index-time", indexSec, "s");
        Report("index-throughput", gbs, "GB/s");

        // 4. edits
        using (var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            var rnd = new Random(7);
            int n = Math.Max(samples, 1000);
            var offsets = Enumerable.Range(0, n).Select(_ => doc.Source.AlignToLineStart(rnd.NextInt64(0, length))).ToArray();
            var ins = new List<double>(); var undo = new List<double>(); var redo = new List<double>();
            for (int i = 0; i < n; i++)
            {
                string text = $"INS-{i} inserted line\n";
                long t0 = Stopwatch.GetTimestamp();
                doc.Insert(offsets[i], text);
                ins.Add(Ms(Stopwatch.GetTimestamp() - t0) * 1000);
            }
            for (int i = 0; i < n; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                doc.Undo();
                undo.Add(Ms(Stopwatch.GetTimestamp() - t0) * 1000);
            }
            for (int i = 0; i < n; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                doc.Redo();
                redo.Add(Ms(Stopwatch.GetTimestamp() - t0) * 1000);
            }
            Mem("after-1000-inserts");
            Console.WriteLine($"pieces after {n} inserts: {doc.PieceCount}, length delta={doc.ByteLength - length}");
            Report("insert-middle", ins, "us");
            Report("undo-insert", undo, "us");
            Report("redo-insert", redo, "us");
        }
        using (var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            var rnd = new Random(8);
            int n = Math.Max(samples, 1000);
            var del = new List<double>(); var undoDel = new List<double>();
            var offsets = Enumerable.Range(0, n).Select(_ => doc.Source.AlignToLineStart(rnd.NextInt64(0, length - 4096))).Distinct().OrderByDescending(x => x).ToArray();
            foreach (var off in offsets)
            {
                long t0 = Stopwatch.GetTimestamp();
                doc.Delete(off, 20);
                del.Add(Ms(Stopwatch.GetTimestamp() - t0) * 1000);
            }
            for (int i = 0; i < offsets.Length; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                doc.Undo();
                undoDel.Add(Ms(Stopwatch.GetTimestamp() - t0) * 1000);
            }
            Report("delete-middle", del, "us");
            Report("undo-delete", undoDel, "us");
            Console.WriteLine($"after undoing all deletes length equals original: {doc.ByteLength == length}");
        }

        // 5. streaming save of an edited document, byte-verified with an independent hash
        for (int i = 0; i < Math.Min(reps, length > (5L << 30) ? 1 : reps); i++)
            await SaveScenario(path, name, length, inPlace && i == 0, i, Rec, Mem);
        Console.WriteLine($"final peak: {NksHub.Textida.Bench.Mem.Snapshot()}");
    }

    private static async Task SaveScenario(string path, string name, long length, bool inPlace, int rep, Action<string, int, double, string> rec, Action<string> mem)
    {
        Directory.CreateDirectory(Paths.Scratch);
        string work = path;
        if (inPlace)
        {
            work = Path.Combine(Paths.Scratch, "inplace-" + name);
            File.Copy(path, work, true);
        }
        string outPath = inPlace ? work : Path.Combine(Paths.Scratch, "save-out-" + name);
        using var doc = PagedDocument.Open(work, new MappedFileSourceOptions { StartIndexing = false });
        long a25 = doc.Source.AlignToLineStart(length / 4);
        long e25 = doc.Source.AlignToLineStart(a25 + (1 << 20));
        long a50 = doc.Source.AlignToLineStart(length / 2);
        byte[] inserted = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("inserted by the save benchmark, ěščřž\n", 30)));
        doc.Insert(a50, inserted);
        doc.Delete(a25, e25 - a25);
        long expectedLength = length + inserted.Length - (e25 - a25);

        if (rep == 0)
        {
            string refCopy = Path.Combine(Paths.Scratch, "refcopy-" + name);
            var swCopy = Stopwatch.StartNew();
            File.Copy(path, refCopy, true);
            double copySec = swCopy.Elapsed.TotalSeconds;
            Console.WriteLine($"reference File.Copy of the original: {copySec:F2}s, {length / copySec / 1e9:F2} GB/s");
            rec("refcopy-throughput", 0, length / copySec / 1e9, "GB/s");
            File.Delete(refCopy);
        }
        var swSave = Stopwatch.StartNew();
        await (inPlace ? doc.SaveAsync() : doc.SaveAsync(outPath));
        double sec = swSave.Elapsed.TotalSeconds;
        long outLen = new FileInfo(outPath).Length;
        Console.WriteLine($"save rep {rep} ({(inPlace ? "in place" : "to new file")}): {sec:F2}s, {outLen / sec / 1e9:F2} GB/s, bytes={outLen} expected={expectedLength}");
        rec("save-time", rep, sec, "s");
        rec("save-throughput", rep, outLen / sec / 1e9, "GB/s");
        mem("after-save");

        var swVerify = Stopwatch.StartNew();
        var actualTask = Task.Run(() => Hashing.Sha256File(outPath));
        var expectedTask = Task.Run(() => HashSegments(path, [(0, a25), (e25, a50 - e25)], inserted, a50, length - a50));
        string actual = await actualTask, expected = await expectedTask;
        Console.WriteLine($"save verify: {(actual == expected && outLen == expectedLength ? "OK bytes identical" : "MISMATCH")} sha256={actual} ({swVerify.Elapsed.TotalSeconds:F1}s)");
        if (actual != expected || outLen != expectedLength) Environment.ExitCode = 1;
        rec("save-verified", rep, actual == expected ? 1 : 0, "bool");
        if (inPlace)
        {
            var l = await doc.ReadLinesAtAsync(0, 2);
            Console.WriteLine($"in-place: document remapped, modified={doc.IsModified}, length={doc.ByteLength}, first line='{l[0][..Math.Min(30, l[0].Length)]}'");
        }
        doc.Dispose();
        File.Delete(outPath);
    }

    private static string HashSegments(string path, (long Start, long Length)[] before, byte[] inserted, long tailStart, long tailLength)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fh = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var buf = new byte[8 << 20];
        void Feed(long start, long len)
        {
            while (len > 0)
            {
                int n = RandomAccess.Read(fh, buf.AsSpan(0, (int)Math.Min(buf.Length, len)), start);
                if (n <= 0) throw new EndOfStreamException();
                h.AppendData(buf, 0, n);
                start += n;
                len -= n;
            }
        }
        // before[0] = [0, a25); before[1] = [e25, a50); then the inserted text; then the tail
        Feed(before[0].Start, before[0].Length);
        Feed(before[1].Start, before[1].Length);
        h.AppendData(inserted);
        Feed(tailStart, tailLength);
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace CrashHarness;

/// <summary>Deterministic text of any length that can be generated for any range, so a model of a multi-GiB document needs no memory.</summary>
public static class LargeText
{
    public const int LineBytes = 64;

    /// <summary>Fills <paramref name="dest"/> with the bytes of text number <paramref name="seed"/> that start at <paramref name="offset"/>. Lines of 64 bytes: a number, a pseudo-random word and a line feed.</summary>
    public static void Fill(long seed, long offset, Span<byte> dest)
    {
        Span<byte> line = stackalloc byte[LineBytes];
        for (long done = 0; done < dest.Length;)
        {
            long pos = offset + done;
            Line(seed, pos / LineBytes, line);
            int col = (int)(pos % LineBytes);
            int n = (int)Math.Min(LineBytes - col, dest.Length - done);
            line.Slice(col, n).CopyTo(dest[(int)done..]);
            done += n;
        }
    }

    private static void Line(long seed, long number, Span<byte> line)
    {
        long digits = number;
        for (int i = 11; i >= 0; i--, digits /= 10) line[i] = (byte)('0' + digits % 10);
        line[12] = (byte)' ';
        ulong h = (ulong)seed * 0x9E3779B97F4A7C15UL + (ulong)number * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
        for (int j = 13; j < 63;)
        {
            h = h * 6364136223846793005UL + 1442695040888963407UL;
            ulong bits = h >> 11;
            for (int k = 0; k < 7 && j < 63; k++, j++, bits >>= 8) line[j] = (byte)('a' + (int)(bits & 0xFF) % 26);
        }
        line[63] = (byte)'\n';
    }

    public static void WriteFile(string path, long length)
    {
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < length;)
        {
            int n = (int)Math.Min(buffer.Length, length - at);
            Fill(0, at, buffer.AsSpan(0, n));
            fs.Write(buffer, 0, n);
            at += n;
        }
    }
}

/// <summary>The content of a document as a list of generated pieces; the independent expectation of a scenario.</summary>
public sealed class LargeModel
{
    private List<(long Seed, long Start, long Length)> _segments;

    public LargeModel(long originalLength)
    {
        _segments = originalLength > 0 ? [(0, 0, originalLength)] : [];
        Length = originalLength;
    }

    private LargeModel(List<(long, long, long)> segments, long length)
    {
        _segments = segments;
        Length = length;
    }

    public long Length { get; private set; }

    public LargeModel Clone() => new([.. _segments], Length);

    private List<(long Seed, long Start, long Length)> Slice(long from, long to)
    {
        var part = new List<(long, long, long)>();
        long pos = 0;
        foreach (var (seed, start, length) in _segments)
        {
            long a = Math.Max(from, pos), b = Math.Min(to, pos + length);
            if (b > a) part.Add((seed, start + (a - pos), b - a));
            pos += length;
        }
        return part;
    }

    /// <summary>Replaces <paramref name="deleteLength"/> bytes at <paramref name="offset"/> by <paramref name="insertLength"/> bytes of text number <paramref name="seed"/>.</summary>
    public void Replace(long offset, long deleteLength, long seed, long insertLength)
    {
        var next = Slice(0, offset);
        if (insertLength > 0) next.Add((seed, 0, insertLength));
        next.AddRange(Slice(offset + deleteLength, Length));
        _segments = next;
        Length += insertLength - deleteLength;
    }

    /// <summary>
    /// A Replace All: edits in ascending order and in the coordinates of the content before any of them, each inside one piece, in one pass over the pieces
    /// (one <see cref="Replace"/> per edit would copy the list of pieces for every one of them).
    /// </summary>
    public void ReplaceAscending(IReadOnlyList<(long Offset, long Delete, long Seed, long InsertLength)> edits)
    {
        var next = new List<(long Seed, long Start, long Length)>(_segments.Count + 2 * edits.Count);
        int e = 0;
        long pos = 0;
        foreach (var (seed, start, length) in _segments)
        {
            long end = pos + length, taken = 0;
            while (e < edits.Count && edits[e].Offset < end)
            {
                var (offset, delete, editSeed, insert) = edits[e++];
                if (offset + delete > end) throw new InvalidOperationException("an edit of a Replace All crosses two pieces of the model");
                long before = offset - (pos + taken);
                if (before > 0) next.Add((seed, start + taken, before));
                if (insert > 0) next.Add((editSeed, 0, insert));
                taken = offset + delete - pos;
            }
            if (taken < length) next.Add((seed, start + taken, length - taken));
            pos = end;
        }
        for (; e < edits.Count; e++)
        {
            if (edits[e].Offset != pos || edits[e].Delete != 0) throw new InvalidOperationException("an edit of a Replace All is past the end of the model");
            if (edits[e].InsertLength > 0) next.Add((edits[e].Seed, 0, edits[e].InsertLength));
        }
        Length += edits.Sum(x => x.InsertLength - x.Delete);
        _segments = next;
    }

    public string Sha256()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        foreach (var (seed, start, length) in _segments)
            for (long done = 0; done < length;)
            {
                int n = (int)Math.Min(buffer.Length, length - done);
                LargeText.Fill(seed, start + done, buffer.AsSpan(0, n));
                hash.AppendData(buffer, 0, n);
                done += n;
            }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>
/// One edit of the scripts, placed by thousandths of the length at the time it is made. The op named <c>bulk</c> is a Replace All: <see cref="InsertLength"/> edits in
/// one change set, edit number k replaces the byte at column 12 of line k (the space after the line number) by 1 to 3 bytes of the text number <see cref="Seed"/> + k.
/// </summary>
public sealed record LargeOp(string Name, int Permille, long Delete, long InsertLength, long Seed)
{
    public bool IsBulk => Name == "bulk";

    /// <summary>Takes back the op before it with the undo of the document; the model returns to the state before that op.</summary>
    public bool IsUndo => Name == "undo";

    public (long Offset, long Delete, long Insert) Resolve(long length)
    {
        long offset = length / 1000 * Permille + length % 1000 * Permille / 1000;
        return (offset, Math.Min(Delete, length - offset), InsertLength);
    }

    /// <summary>The edits of a bulk op, ascending, as offsets in the document before the op and the bytes that replace one byte each.</summary>
    public List<(long Offset, byte[] Bytes)> BulkEdits()
    {
        var edits = new List<(long, byte[])>((int)InsertLength);
        for (long k = 0; k < InsertLength; k++)
        {
            var bytes = new byte[1 + k % 3];
            LargeText.Fill(Seed + k, 0, bytes);
            edits.Add((k * LargeText.LineBytes + 12, bytes));
        }
        return edits;
    }

    /// <summary>Applies the op to the model.</summary>
    public void ApplyBulk(LargeModel model) =>
        model.ReplaceAscending([.. BulkEdits().Select((e, k) => (e.Offset, 1L, Seed + k, (long)e.Bytes.Length))]);
}

/// <summary>
/// Kill test of a large paged document with a journal. A child process edits a file (large inserts that spill to disk, a burst of small edits),
/// streams a save while it keeps editing, and dies at a named point (or is killed at a random moment). The parent recovers the journal and compares the
/// document and the file with a model that is computed from the same script and never from the product.
/// </summary>
public static class LargeScenario
{
    public const long SpillBudget = 256 << 10;
    public const int DefaultBig = 3 << 20;
    /// <summary>Edits of the Replace All of the script: more than <see cref="JournalPlaceholders.MaxInlineEditCount"/>, so the journal writes them as a list.</summary>
    public const int BulkEdits = 6000;

    public static readonly string[] Points = ["spill-written", "journal-flushed", "mid-write", "temp-written", "before-swap", "mid-swap", "after-swap", "saved", "post-save", "cancel"];

    public static (List<LargeOp> Before, List<LargeOp> During, List<LargeOp> After) Plan(int burst, int big)
    {
        var rng = new Random(77);
        var before = new List<LargeOp> { new("small", 100, 0, 100, 1001), new("delete", 300, 50, 0, 0) };
        for (int i = 0; i < burst; i++) before.Add(new("tick", rng.Next(0, 1000), 0, 1, 2000 + i));
        before.Add(new("bulk", 0, 0, BulkEdits, 3000)); // a Replace All: its edits go to the journal as a list in a payload file
        before.Add(new("big-1", 500, 0, big, 1003));
        before.Add(new("replace", 700, 20, 30, 1004));
        before.Add(new("big-2", 50, 0, big * 2L / 3, 1005));
        var during = new List<LargeOp> { new("big-3", 900, 0, big * 2L / 3, 1006), new("small-2", 200, 0, 10, 1007), new("delete-2", 400, 200, 0, 0), new("undo", 0, 0, 0, 0) };
        return (before, during, [new("after", 600, 0, 7, 1008)]);
    }

    private static void Kill(JournalSession session, bool flush)
    {
        if (flush) session.Flush();
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>Ends the process on the worker thread when the temporary file is half written, while the main thread keeps editing.</summary>
    private sealed class KillHalfway(JournalSession session) : IProgress<SaveProgress>
    {
        public void Report(SaveProgress progress)
        {
            if (progress.Written * 2 >= progress.Total) Kill(session, flush: true);
        }
    }

    /// <summary>Edits, saves while editing and dies at <paramref name="point"/> (<c>slow</c> sleeps between steps so a kill from outside lands anywhere).</summary>
    public static int Child(string dir, int seed, string point, long size, int burst)
    {
        var timing = new Random(seed);
        void Pause()
        {
            if (point == "slow") Thread.Sleep(timing.Next(1, 20));
        }

        var factory = new PagedBackendFactory(startIndexing: false, spillBudgetBytes: SpillBudget, spillDirectory: Path.Combine(dir, "spill"));
        using var session = JournalSession.Start(Path.Combine(dir, "journal"), name => name == "paged" ? factory : null);
        using var doc = Doc.Open(Path.Combine(dir, "data", "doc.txt"), factory);
        session.Track(doc);
        session.Flush();
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        stdout.WriteLine("ready");

        void Do(LargeOp op)
        {
            if (op.IsUndo)
            {
                if (doc.Undo() is null) throw new InvalidOperationException("the undo was refused");
                Pause();
                return;
            }
            if (op.IsBulk)
            {
                var edits = op.BulkEdits().Select(e => new ByteEdit(new ByteRange(e.Offset, e.Offset + 1), e.Bytes)).ToList();
                if (!doc.Apply(edits).Applied) throw new InvalidOperationException($"{op.Name} was refused");
                Pause();
                return;
            }
            var (offset, delete, insert) = op.Resolve(doc.ByteLength);
            var bytes = new byte[insert];
            LargeText.Fill(op.Seed, 0, bytes);
            if (!doc.Replace(new ByteRange(offset, offset + delete), bytes).Applied) throw new InvalidOperationException($"{op.Name} was refused");
            Pause();
        }

        var (before, during, after) = Plan(burst, DefaultBig);
        foreach (var op in before)
        {
            if (op.Name == "big-1" && point == "spill-written")
            {
                session.Flush();
                Do(op);
                Kill(session, flush: false);
            }
            Do(op);
        }
        if (point == "journal-flushed")
        {
            // what the kill leaves on disk: the undo history is spilled in a scratch file (it dies with the process), the added text is spilled in a payload file
            File.WriteAllText(Path.Combine(dir, "spills.txt"), Directory.GetFiles(Path.Combine(dir, "spill"), "*-undo.spill").Length.ToString());
            Kill(session, flush: true);
        }

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, temp) =>
        {
            switch (step)
            {
                case "temp-written":
                    gate.Wait();
                    if (point == "cancel") doc.CancelSave();
                    if (point == "temp-written") Kill(session, flush: true);
                    break;
                case "before-swap":
                    if (point == "mid-swap")
                    {
                        File.Move(doc.FilePath!, FileSwap.BackupOf(temp)); // where Windows ReplaceFile stands between its two renames
                        Kill(session, flush: true);
                    }
                    if (point == "before-swap") Kill(session, flush: true);
                    break;
                case "after-swap":
                    if (point == "after-swap") Kill(session, flush: true);
                    break;
                case "saved":
                    if (point == "saved") Kill(session, flush: true);
                    break;
            }
        };
        if (point == "mid-write") doc.ProgressIntervalMs = 0; // a report after every MiB, so the kill lands inside the write of a small file
        var save = doc.SaveAsync(progress: point == "mid-write" ? new KillHalfway(session) : null);
        foreach (var op in during) Do(op);
        gate.Set();
        try { save.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (point == "cancel") { }
        foreach (var op in after) Do(op);
        if (point == "post-save") Kill(session, flush: true);
        session.Flush();
        return 0;
    }

    private static ProcessStartInfo ChildStart(string dir, int seed, string point, long size, int burst)
    {
        var self = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var a in new[] { "large-child", dir, seed.ToString(), point, size.ToString(), burst.ToString() }) psi.ArgumentList.Add(a);
        return psi;
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static string HashDocument(Doc doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            if (n == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Returns null when the recovered document and the file match the model, else a description of the difference.</summary>
    public static string? RunIteration(string dir, int seed, string point, int killAfterReadyMs, long size, int burst)
    {
        string data = Path.Combine(dir, "data"), journal = Path.Combine(dir, "journal"), spill = Path.Combine(dir, "spill");
        Directory.CreateDirectory(data);
        string file = Path.Combine(data, "doc.txt");
        LargeText.WriteFile(file, size);
        string originalHash = HashFile(file);

        using var proc = Process.Start(ChildStart(dir, seed, point, size, burst))!;
        int childPid = proc.Id;
        var error = proc.StandardError.ReadToEndAsync();
        string? first = null;
        var ready = Task.Run(() => first = proc.StandardOutput.ReadLine());
        if (!ready.Wait(TimeSpan.FromSeconds(120))) { proc.Kill(true); return "child never became ready: " + error.Result; }
        if (first == "ready" && killAfterReadyMs >= 0)
        {
            Thread.Sleep(killAfterReadyMs);
            if (!proc.HasExited) proc.Kill(true);
        }
        if (!proc.WaitForExit(point == "slow" ? 600_000 : 1_800_000)) { proc.Kill(true); return "child did not stop"; }

        var (before, during, after) = Plan(burst, DefaultBig);
        var script = before.Concat(during).Concat(after).ToList();
        var states = new List<LargeModel> { new(size) };
        foreach (var op in script)
        {
            if (op.IsUndo)
            {
                states.Add(states[^2].Clone());
                continue;
            }
            var next = states[^1].Clone();
            if (op.IsBulk) op.ApplyBulk(next);
            else
            {
                var (offset, delete, insert) = op.Resolve(next.Length);
                next.Replace(offset, delete, op.Seed, insert);
            }
            states.Add(next);
        }
        var hashes = new Dictionary<int, string>();
        string StateHash(int k) => hashes.TryGetValue(k, out var h) ? h : hashes[k] = states[k].Sha256();

        int a = before.Count, b = during.Count, c = after.Count, big1 = before.FindIndex(o => o.Name == "big-1");
        int[] docStates, fileStates;
        switch (point)
        {
            case "spill-written": (docStates, fileStates) = ([big1, big1 + 1], [0]); break;
            case "journal-flushed": (docStates, fileStates) = ([a], [0]); break;
            case "mid-write": (docStates, fileStates) = ([.. Enumerable.Range(a, b + 1)], [0]); break; // the kill meets the edits of the save at any of them
            case "temp-written" or "before-swap" or "mid-swap": (docStates, fileStates) = ([a + b], [0]); break;
            case "after-swap" or "saved": (docStates, fileStates) = ([a + b], [a]); break;
            case "post-save": (docStates, fileStates) = ([a + b + c], [a]); break;
            case "cancel": (docStates, fileStates) = ([a + b + c], [0]); break; // the child ends normally after one more edit
            default: (docStates, fileStates) = ([.. Enumerable.Range(0, a + b + c + 1)], [0, a]); break;
        }
        if (originalHash != StateHash(0)) return "the harness wrote an unexpected original";

        if (point == "journal-flushed")
        {
            if (Directory.GetFiles(Path.Combine(journal, "payload"), "*-add-*.payload").Length == 0) return "the large inserts did not reach the journal as references to a spill file";
            if (Directory.GetFiles(Path.Combine(journal, "payload"), "*-blob-*.payload").Length == 0) return "the Replace All did not reach the journal as a list in a payload file";
            if (burst >= 2500 && File.ReadAllText(Path.Combine(dir, "spills.txt")) == "0") return "the undo history did not spill in the child";
        }
        var factory = new PagedBackendFactory(startIndexing: false, spillBudgetBytes: SpillBudget, spillDirectory: spill);
        using var session = JournalSession.Start(journal, name => name == "paged" ? factory : null);
        var recovered = session.Recovery.Documents;
        if (recovered.Count != 1) return $"{recovered.Count} documents were recovered: {string.Join("; ", session.Recovery.Notes)}";
        var r = recovered[0];
        if (r.Status is not (RecoveryStatus.Restored or RecoveryStatus.Clean)) return $"recovery {r.Status}: {r.Error}";
        var doc = r.Document!;

        string docHash = HashDocument(doc), fileHash = HashFile(file);
        int docIndex = docStates.FirstOrDefault(k => StateHash(k) == docHash, -1);
        int fileIndex = fileStates.FirstOrDefault(k => StateHash(k) == fileHash, -1);
        if (docIndex < 0) return $"the recovered document ({doc.ByteLength} bytes) is none of the states {string.Join(",", docStates)} ({string.Join(",", docStates.Select(k => states[k].Length))} bytes)";
        if (fileIndex < 0) return $"the file is none of the states {string.Join(",", fileStates)} ({new FileInfo(file).Length} bytes)";
        if (fileIndex == a && docIndex < a) return "the file holds a save the document does not know";
        if (Directory.GetFiles(data, "*.tmp").Length > 0) return "temporary files left behind: " + string.Join(", ", Directory.GetFiles(data, "*.tmp").Select(Path.GetFileName));

        string copy = Path.Combine(dir, "copy.txt");
        var saved = doc.Save(copy);
        if (!saved.Saved || HashFile(copy) != docHash) return "the recovered document does not save as what it holds";
        if (HashFile(file) != fileHash) return "saving the copy changed the original";

        if (Directory.Exists(spill) && Directory.GetFiles(spill, $"{childPid}-*.spill").Length > 0) return "a spill file of the killed process was not removed";
        doc.Dispose();
        return null;
    }

    public static int RunAll(int iterations, int parallel, int seed0, string work, long size, int burst, string[]? points = null)
    {
        Directory.CreateDirectory(work);
        var jobs = new List<(string Name, string Point, int Seed, int Kill)>();
        int seed = seed0;
        foreach (var point in points ?? Points) jobs.Add(($"point {point}", point, seed++, -1));
        var rng = new Random(seed0);
        for (int i = 0; i < iterations; i++) jobs.Add(($"random kill #{i}", "slow", seed++, rng.Next(50, 1500)));
        Console.WriteLine($"work dir {work}; {jobs.Count} runs ({(points ?? Points).Length} injected crash points, {iterations} random kills), document {size} bytes");
        var failures = new List<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            var (name, point, s, kill) = jobs[i];
            string dir = Path.Combine(work, $"l{i:D3}");
            string? problem;
            try { problem = RunIteration(dir, s, point, kill, size, burst); }
            catch (Exception e) { problem = "harness error: " + e; }
            lock (failures)
            {
                Console.WriteLine($"[{i + 1:D3}/{jobs.Count}] {name} {(problem is null ? "ok" : "FAIL " + problem)}");
                if (problem is not null) failures.Add($"{name}: {problem}");
            }
        });
        Console.WriteLine($"runs {jobs.Count}, failures {failures.Count}");
        foreach (var f in failures) Console.WriteLine("FAILED " + f);
        if (failures.Count == 0)
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
        return failures.Count == 0 ? 0 : 1;
    }
}

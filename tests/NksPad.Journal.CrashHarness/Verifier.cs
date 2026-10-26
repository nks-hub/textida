using System.Diagnostics;
using System.Reflection;
using NksPad.Core.Documents;
using NksPad.Core.Journal;

namespace CrashHarness;

public sealed record IterationResult(int Iteration, int Seed, bool Ok, string Message, int Ops, long LossMs, long JournalLagMs, bool Lost, bool Torn, int Saves, int Loose);

/// <summary>Kills a journaling child at a random moment and checks the recovered documents against an oracle run of the same operations.</summary>
public static class Verifier
{
    public const long LossLimitMs = 2000;

    private sealed class StopRun : Exception;

    private sealed record Candidate(int Op, string Hash);

    public static int RunAll(int iterations, int parallel, int seed0, string work)
    {
        Directory.CreateDirectory(work);
        Console.WriteLine($"work dir {work}; {iterations} iterations, {parallel} in parallel");
        var results = new IterationResult[iterations];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            IterationResult r;
            try { r = RunIteration(i, seed0 + i, Path.Combine(work, $"it{i:D3}")); }
            catch (Exception e) { r = new(i, seed0 + i, false, "harness error: " + e, 0, 0, 0, false, false, 0, 0); }
            results[i] = r;
            Console.WriteLine($"[{i + 1:D3}/{iterations}] seed {r.Seed} {(r.Ok ? "ok  " : "FAIL")} ops {r.Ops} loss {r.LossMs} ms lag {r.JournalLagMs} ms {(r.Ok ? "" : r.Message)}");
        });

        int failed = results.Count(r => !r.Ok);
        Console.WriteLine();
        Console.WriteLine($"iterations {iterations}, failures {failed}, wall {sw.Elapsed.TotalSeconds:F0} s");
        Console.WriteLine($"operations before the kills {results.Sum(r => (long)r.Ops)}, saves {results.Sum(r => r.Saves)}");
        Console.WriteLine($"iterations that lost state {results.Count(r => r.Lost)}; max loss {results.Max(r => r.LossMs)} ms (limit {LossLimitMs}); max journal lag {results.Max(r => r.JournalLagMs)} ms");
        Console.WriteLine($"torn or corrupt journal tails detected {results.Count(r => r.Torn)}; iterations with relaxed undo comparison {results.Count(r => r.Loose > 0)}");
        foreach (var r in results.Where(r => !r.Ok)) Console.WriteLine($"FAILED it{r.Iteration:D3} seed {r.Seed}: {r.Message}");
        if (failed == 0)
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
        return failed == 0 ? 0 : 1;
    }

    private static ProcessStartInfo ChildStart(string dir, int seed)
    {
        var self = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        psi.ArgumentList.Add("child");
        psi.ArgumentList.Add(dir);
        psi.ArgumentList.Add(seed.ToString());
        return psi;
    }

    public static IterationResult RunIteration(int iteration, int seed, string dir)
    {
        Directory.CreateDirectory(dir);
        var kill = new Random(seed * 7919 + 13);
        using var proc = Process.Start(ChildStart(dir, seed))!;
        var times = new List<long>();
        long readyAt = -1;
        var errors = new System.Text.StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errors) errors.AppendLine(e.Data); };
        proc.BeginErrorReadLine();
        var reader = Task.Run(() =>
        {
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                var parts = line.Split(' ');
                if (parts.Length != 2 || !long.TryParse(parts[1], out long t)) continue;
                lock (times)
                {
                    if (parts[0] == "ready") readyAt = t;
                    else if (int.TryParse(parts[0], out int op) && op == times.Count) times.Add(t);
                }
            }
        });

        var wait = Stopwatch.StartNew();
        while (Volatile.Read(ref readyAt) < 0 && !proc.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(5);
        if (Volatile.Read(ref readyAt) < 0)
        {
            if (!proc.HasExited) proc.Kill(true);
            proc.WaitForExit();
            return Fail(iteration, seed, "child never became ready: " + errors);
        }
        int delay = kill.Next(10) == 0 ? kill.Next(60) : 150 + kill.Next(3400);
        Thread.Sleep(delay);
        if (proc.HasExited) return Fail(iteration, seed, "child exited by itself: " + errors);
        proc.Kill(true);
        proc.WaitForExit();
        reader.Wait();

        long[] opTimes;
        lock (times) opTimes = [.. times];
        int k = opTimes.Length - 1;
        return Verify(iteration, seed, dir, k, opTimes, readyAt);
    }

    private static IterationResult Fail(int iteration, int seed, string message) => new(iteration, seed, false, message, 0, 0, 0, false, false, 0, 0);

    private static string JournalOf(string dir) => Directory.GetFiles(Path.Combine(dir, "journal"), "session-*.njl").Single();

    private static IterationResult Verify(int iteration, int seed, string dir, int k, long[] opTimes, long readyAt)
    {
        IterationResult Bad(string m, int ops = 0) => new(iteration, seed, false, m, ops, 0, 0, false, false, 0, 0);

        // oracle: the same operations without a journal, recording the state after every change
        var candidates = new List<Candidate>();
        int saves, loose = 0;
        using (var model = Scene.Create(Path.Combine(dir, "model"), seed))
        {
            int current = -1;
            candidates.Add(new(-1, model.StateHash()));
            model.DocChanged += _ => candidates.Add(new(current, model.StateHash()));
            var rng = new Random(seed);
            for (int op = 0; op <= k + 1; op++)
            {
                current = op;
                model.RunOp(rng, op, null);
            }
            saves = model.FileStates.Sum(s => s.Count(x => x.Op >= 0 && x.Op <= k + 1));
            loose = model.Loose.Count(l => l);

            // the files of the user: only a complete save may ever have changed them
            for (int d = 1; d < Scene.DocCount; d++)
            {
                var actual = Scene.Hex(File.ReadAllBytes(Path.Combine(dir, "data", Path.GetFileName(model.FilePathOf(d)))));
                if (!model.FileStates[d].Any(s => s.Hash == actual && s.Op <= k + 1)) return Bad($"user file of document {d} is not the result of any save");
            }
        }

        var recovery = JournalRecovery.Recover(JournalOf(dir), Scene.Resolve);
        try
        {
            if (recovery.Documents.Count != Scene.DocCount) return Bad($"{recovery.Documents.Count} documents recovered; notes: {string.Join("; ", recovery.Notes)}", k + 1);
            var bad = recovery.Documents.Where(d => d.Status is not (RecoveryStatus.Restored or RecoveryStatus.Clean)).ToList();
            if (bad.Count > 0) return Bad("not recovered: " + string.Join(", ", bad.Select(b => $"{b.Status} {b.Error}")), k + 1);

            string recovered = Scene.StateHash(recovery.Documents);
            int match = candidates.FindLastIndex(c => c.Hash == recovered && c.Op <= k + 1);
            if (match < 0) {
                string Short(string h) => string.Join(" | ", h.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => { var p = x.Split(':'); return $"{p[0]}:{p[1]}:{p[2]}:{p[3][..6]}"; }));
                int lastIdx = candidates.FindLastIndex(c => c.Op <= k + 1);
                return Bad($"recovered state matches no state of the oracle ({candidates.Count} candidates, last acknowledged op {k}); recovered {Short(recovered)}; last oracle {Short(candidates[lastIdx].Hash)} (op {candidates[lastIdx].Op}); tail {recovery.Tail}, records {recovery.Records}, notes {string.Join("; ", recovery.Notes)}", k + 1);
            }

            long TimeOf(int op) => op < 0 ? readyAt : opTimes[Math.Min(op, k)];
            int last = candidates.FindLastIndex(c => c.Op <= k + 1);
            bool lost = match != last && candidates[match].Hash != candidates[last].Hash;
            long loss = lost ? TimeOf(candidates[last].Op) - TimeOf(candidates[match].Op) : 0;
            long lag = recovery.LastRecord is { } lr && k >= 0 ? Math.Max(0, opTimes[k] - lr.ToUnixTimeMilliseconds()) : 0;
            if (loss > LossLimitMs) return Bad($"lost {loss} ms of work (limit {LossLimitMs})", k + 1);
            if (lost && lag > LossLimitMs) return Bad($"journal ends {lag} ms before the last operation", k + 1);

            string? parity = CompareUndo(seed, k, candidates, match, recovery);
            if (parity is not null) return Bad(parity, k + 1);

            var tear = TearCheck(dir, seed, candidates, k, out var tornTail);
            if (tear is not null) return Bad(tear, k + 1);

            string? again = AdoptAndCheck(dir, recovered);
            if (again is not null) return Bad(again, k + 1);
            return new(iteration, seed, true, "", k + 1, loss, lag, lost, tornTail, saves, loose);
        }
        finally
        {
            foreach (var d in recovery.Documents) d.Document?.Dispose();
        }
    }

    /// <summary>Runs the oracle again up to the matching state and walks both undo stacks down together.</summary>
    private static string? CompareUndo(int seed, int k, List<Candidate> candidates, int match, RecoveryResult recovery)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nkspad-undo-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var model = Scene.Create(dir, seed);
            int ordinal = 0;
            model.DocChanged += _ =>
            {
                if (++ordinal == match) throw new StopRun();
            };
            var rng = new Random(seed);
            if (match > 0)
                try
                {
                    for (int op = 0; op <= k + 1; op++) model.RunOp(rng, op, null);
                }
                catch (StopRun) { }

            for (int d = 0; d < Scene.DocCount; d++)
            {
                var rec = recovery.Documents[d].Document!;
                var mod = model.Docs[d];
                if (model.Loose[d]) continue;
                if (rec.IsModified != mod.IsModified) return $"document {d}: unsaved state differs (recovered {rec.IsModified}, oracle {mod.IsModified})";
                for (int guard = 0; rec.CanUndo; guard++)
                {
                    if (!mod.CanUndo || guard > 100_000) return $"document {d}: recovered undo stack is longer than the oracle's";
                    rec.Undo();
                    mod.Undo();
                    if (!rec.ReadBytes(new ByteRange(0, rec.ByteLength)).AsSpan().SequenceEqual(mod.ReadBytes(new ByteRange(0, mod.ByteLength))))
                        return $"document {d}: undo step {guard} gives different bytes";
                }
            }
            return null;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Emulates a power loss that persisted only part of the last writes: the journal is cut (or one byte is damaged) somewhere after
    /// everything that was fsynced for certain (document starts, seeds and save intents). Recovery must still give a state the oracle passed through.
    /// </summary>
    private static string? TearCheck(string dir, int seed, List<Candidate> candidates, int k, out bool tornTail)
    {
        tornTail = false;
        var bytes = File.ReadAllBytes(JournalOf(dir));
        long floor = JournalFormat.FileHeaderSize, offset = floor;
        using (var reader = new JournalReader(JournalOf(dir)))
            while (reader.TryRead(out var rec))
            {
                offset += JournalFormat.RecordHeaderSize(JournalFormat.CurrentVersion) + rec.Payload.Length;
                if (rec.Type is RecordType.DocOpen or RecordType.DocSeed or RecordType.DocSaveIntent) floor = offset;
            }
        if (bytes.Length <= floor + 1) return null;

        var rng = new Random(seed * 31 + 5);
        int at = (int)rng.NextInt64(floor, bytes.Length);
        var torn = rng.Next(2) == 0 ? bytes[..at] : (byte[])bytes.Clone();
        if (torn.Length == bytes.Length) torn[at] ^= (byte)(1 << rng.Next(8));
        var tearDir = Path.Combine(dir, "tear");
        Directory.CreateDirectory(tearDir);
        var tornPath = Path.Combine(tearDir, "session-tear.njl");
        File.WriteAllBytes(tornPath, torn);

        var result = JournalRecovery.Recover(tornPath, Scene.Resolve);
        tornTail = result.Tail != JournalTail.Clean;
        try
        {
            if (result.Documents.Count != Scene.DocCount || result.Documents.Any(d => d.Status is not (RecoveryStatus.Restored or RecoveryStatus.Clean)))
                return $"torn journal (cut/damage at {at} of {bytes.Length}, floor {floor}): " + string.Join(", ", result.Documents.Select(d => $"{d.Status} {d.Error}"));
            string hash = Scene.StateHash(result.Documents);
            return candidates.FindLastIndex(c => c.Hash == hash && c.Op <= k + 1) < 0
                ? $"torn journal (at {at} of {bytes.Length}) recovered a state the oracle never had" : null;
        }
        finally
        {
            foreach (var d in result.Documents) d.Document?.Dispose();
        }
    }

    /// <summary>Starts a new session over the crashed journal (adoption), then reads the journal that session leaves behind.</summary>
    private static string? AdoptAndCheck(string dir, string expectedHash)
    {
        var journalDir = Path.Combine(dir, "journal");
        string hash;
        using (var session = JournalSession.Start(journalDir, Scene.Resolve))
        {
            if (session.Recovery.Documents.Any(d => d.Status is not (RecoveryStatus.Restored or RecoveryStatus.Clean))) return "adoption: a document did not come back";
            hash = Scene.StateHash(session.Recovery.Documents);
            foreach (var d in session.Recovery.Documents) d.Document?.Dispose();
            if (hash != expectedHash) return "adoption: the adopted state differs from the recovered one";
        }
        var files = Directory.GetFiles(journalDir, "session-*.njl");
        if (files.Length != 1) return $"adoption left {files.Length} journals";
        var final = JournalRecovery.Recover(files[0], Scene.Resolve);
        try
        {
            if (Scene.StateHash(final.Documents) != expectedHash) return "the journal written by adoption does not give the same state";
            return null;
        }
        finally
        {
            foreach (var d in final.Documents) d.Document?.Dispose();
        }
    }
}

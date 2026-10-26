using System.Diagnostics;
using System.Reflection;
using System.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;

namespace CrashHarness;

/// <summary>
/// Kill test of Replace in Files. A child process plans and applies a replacement over files in several encodings and dies at a named
/// point of the batch (or is killed by the parent at a random moment). The parent then recovers the interrupted batch twice, once
/// finishing it and once rolling it back, and compares every file with bytes computed independently from the original content.
/// </summary>
public static class BatchScenario
{
    public const int FileCount = 8;
    private const string Find = "OLD", Replacement = "NEW-ě";

    private sealed record Sample(string Name, byte[] Original, Encoding Enc, byte[] Bom)
    {
        public byte[] Expected => [.. Bom, .. Enc.GetBytes(Enc.GetString(Original, Bom.Length, Original.Length - Bom.Length).Replace(Find, Replacement, StringComparison.Ordinal))];
    }

    private static List<Sample> Create(string dir, int seed)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(dir);
        var rng = new Random(seed);
        var kinds = new (string Ext, Encoding Enc, byte[] Bom)[]
        {
            ("u8", new UTF8Encoding(false), []), ("u8b", new UTF8Encoding(false), TextCodec.Utf8Bom.Bom), ("u16", new UnicodeEncoding(false, false), TextCodec.Utf16LEBom.Bom),
            ("u16b", new UnicodeEncoding(true, false), TextCodec.Utf16BEBom.Bom), ("cp", Encoding.GetEncoding(1250), []),
        };
        var list = new List<Sample>();
        for (int i = 0; i < FileCount; i++)
        {
            var (ext, enc, bom) = kinds[i % kinds.Length];
            var sb = new StringBuilder();
            for (int line = 0; line < 4000 + rng.Next(4000); line++)
            {
                sb.Append("row ").Append(line).Append(rng.Next(3) == 0 ? " OLD" : " keep").Append(" ěš");
                sb.Append(rng.Next(4) == 0 ? " OLD OLD" : "").Append(rng.Next(5) == 0 ? "\r\n" : "\n");
            }
            var s = new Sample($"f{i}.{ext}", [.. bom, .. enc.GetBytes(sb.ToString())], enc, bom);
            File.WriteAllBytes(Path.Combine(dir, s.Name), s.Original);
            list.Add(s);
        }
        return list;
    }

    /// <summary>Plans and applies the batch; dies at <paramref name="point"/> ("slow" sleeps at every point so a kill from outside lands anywhere).</summary>
    public static int Child(string dir, int seed, string point)
    {
        var timing = new Random(seed);
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var plan = ReplacePlanner.PlanAsync(new SearchPattern(Find), Replacement, new SearchScope([Path.Combine(dir, "data")]),
            new SearchSettings { Walk = new FileWalkOptions { UseIgnoreFiles = false } }).GetAwaiter().GetResult();
        var applier = new ChangeApplier(_ => null, Path.Combine(dir, "journal"))
        {
            FaultPoint = p =>
            {
                if (p == "plan") stdout.WriteLine("ready");
                if (point == "slow") Thread.Sleep(timing.Next(1, 25));
                else if (point == p) Process.GetCurrentProcess().Kill();
            },
        };
        applier.Apply(plan.Proposal);
        return 0;
    }

    private static ProcessStartInfo ChildStart(string dir, int seed, string point)
    {
        var self = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        foreach (var a in new[] { "batch-child", dir, seed.ToString(), point }) psi.ArgumentList.Add(a);
        return psi;
    }

    public static IEnumerable<string> DeterministicPoints()
    {
        yield return "plan";
        for (int i = 0; i < FileCount; i++)
        {
            yield return $"before-swap:{i}";
            yield return $"after-swap:{i}";
            yield return $"after-file:{i}";
        }
        yield return "before-end";
    }

    /// <summary>Returns null when the recovered files match the independent expectation, else a description of the difference.</summary>
    public static string? RunIteration(string dir, int seed, string point, int killAfterReadyMs)
    {
        var samples = Create(Path.Combine(dir, "data"), seed);
        using var proc = Process.Start(ChildStart(dir, seed, point))!;
        var error = proc.StandardError.ReadToEndAsync();
        string? first = null;
        var ready = Task.Run(() => first = proc.StandardOutput.ReadLine());
        if (!ready.Wait(TimeSpan.FromSeconds(60))) { proc.Kill(true); return "child never became ready: " + error.Result; }
        if (first == "ready" && killAfterReadyMs >= 0)
        {
            Thread.Sleep(killAfterReadyMs);
            if (!proc.HasExited) proc.Kill(true);
        }
        if (!proc.WaitForExit(60_000)) { proc.Kill(true); return "child did not stop"; }
        return Verify(dir, samples);
    }

    [ThreadStatic] private static string? _info;

    private static string? Verify(string dir, List<Sample> samples)
    {
        _info = null;
        string data = Path.Combine(dir, "data"), journal = Path.Combine(dir, "journal");
        string? Compare(Func<Sample, byte[]> want, string what)
        {
            foreach (var s in samples)
            {
                var actual = File.ReadAllBytes(Path.Combine(data, s.Name));
                if (!actual.AsSpan().SequenceEqual(want(s))) return $"{what}: {s.Name} differs from the expectation ({actual.Length} vs {want(s).Length} bytes)";
            }
            return Directory.GetFiles(data, "*.tmp").Length > 0 ? $"{what}: temporary files left behind" : null;
        }

        string backup = Path.Combine(dir, "interrupted");
        CopyTree(data, Path.Combine(backup, "data"));
        if (Directory.Exists(journal)) CopyTree(journal, Path.Combine(backup, "journal"));

        var found = BatchRecovery.Find(journal);
        if (found.Count == 0)
        {
            // no pending batch: it never started or it ended, and files are all original or all new
            bool allOld = samples.All(s => File.ReadAllBytes(Path.Combine(data, s.Name)).AsSpan().SequenceEqual(s.Original));
            return allOld || Compare(s => s.Expected, "ended batch") is null ? null : "no pending batch, but the files are neither all original nor all replaced";
        }
        if (found.Count > 1) return $"{found.Count} pending batches";
        _info = string.Join("/", found[0].Files.GroupBy(f => f.State).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));

        using (var pending = found[0])
        {
            var outcomes = pending.Finish();
            if (outcomes.Any(o => o.Status != ChangeStatus.Applied)) return "finish: " + string.Join("; ", outcomes.Where(o => o.Status != ChangeStatus.Applied).Select(o => o.Status + " " + o.Message));
        }
        if (Compare(s => s.Expected, "finish") is { } bad) return bad;
        if (BatchRecovery.Find(journal).Count != 0) return "finish left the journal behind";

        // restore the interrupted state and roll back instead
        foreach (var f in Directory.GetFiles(Path.Combine(backup, "data"))) File.Copy(f, Path.Combine(data, Path.GetFileName(f)), overwrite: true);
        Directory.CreateDirectory(journal);
        foreach (var f in Directory.GetFiles(Path.Combine(backup, "journal"))) File.Copy(f, Path.Combine(journal, Path.GetFileName(f)), overwrite: true);
        var again = BatchRecovery.Find(journal);
        if (again.Count != 1) return "the restored journal was not found again";
        using (var pending = again[0])
        {
            var outcomes = pending.RollBack();
            if (outcomes.Any(o => o.Status == ChangeStatus.Failed || o.Status == ChangeStatus.Conflict)) return "rollback: " + string.Join("; ", outcomes.Where(o => o.Status is ChangeStatus.Failed or ChangeStatus.Conflict).Select(o => o.Status + " " + o.Message));
        }
        return Compare(s => s.Original, "rollback");
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from).Where(f => !f.EndsWith(".lock"))) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
    }

    public static int RunAll(int iterations, int parallel, int seed0, string work)
    {
        Directory.CreateDirectory(work);
        var jobs = new List<(string Name, string Point, int Seed, int Kill)>();
        int seed = seed0;
        foreach (var point in DeterministicPoints()) jobs.Add(($"point {point}", point, seed++, -1));
        var rng = new Random(seed0);
        for (int i = 0; i < iterations; i++)
        {
            bool slow = i % 2 == 0;
            jobs.Add(($"random kill #{i} ({(slow ? "slow" : "fast")})", slow ? "slow" : "none", seed++, slow ? rng.Next(5, 400) : rng.Next(0, 60)));
        }
        Console.WriteLine($"work dir {work}; {jobs.Count} runs ({DeterministicPoints().Count()} injected crash points, {iterations} random kills)");
        var failures = new List<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            var (name, point, s, kill) = jobs[i];
            string dir = Path.Combine(work, $"b{i:D3}");
            string? problem;
            string? info = null;
            try { problem = RunIteration(dir, s, point, kill); info = _info; }
            catch (Exception e) { problem = "harness error: " + e; }
            lock (failures)
            {
                Console.WriteLine($"[{i + 1:D3}/{jobs.Count}] {name} {(problem is null ? "ok" : "FAIL " + problem)} [{info ?? "no pending batch"}]");
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

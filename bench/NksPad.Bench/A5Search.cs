using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NksPad.Core;

namespace NksPad.Bench;

/// <summary>Scenario A5: NksPad.Core search against ripgrep --json on the same data.</summary>
internal static class A5Search
{
    private sealed record Pat(string Label, string Text, bool Regex);

    private static readonly Pat[] FilePatterns =
    [
        new("rare-literal", Corpus.RareLog, false),
        new("frequent-literal", "timeout", false),
        new("regex", @"status=5\d\d took=\d{4,5}ms", true),
    ];

    private static readonly Pat[] TreePatterns =
    [
        new("rare-literal", Corpus.RareTree, false),
        new("frequent-literal", "TODO", false),
        new("regex", @"Load\w+Async\(\d{3}, ct\)", true),
    ];

    private static string BaselineName = "rg";

    private static double Sec(long t0) => (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;

    public static async Task Run(string[] args)
    {
        string mode = args[1];
        bool count = mode.EndsWith("-count");
        string kind = mode.Replace("-count", "");
        BaselineName = count ? "rg-count" : "rg";
        Func<Pat, string, Task<(double, long)>> baseline = count ? RunRgCount : RunRg;
        using var csv = new Csv($"a5-{mode}.csv", "scenario,target,pattern,engine,rep,value,unit,matches");
        if (mode == "cancel")
        {
            string cname = args[2];
            using var cmf = MappedFile.Open(Path.Combine(Paths.Corpus, cname));
            await Cancel(csv, cname, FilePatterns[1], FilePatterns[2], cmf, Path.Combine(Paths.Corpus, cname), int.Parse(args[3]));
        }
        else if (kind == "file")
        {
            foreach (var name in Corpus.PositionalArgs(args).Skip(1))
            {
                int reps = name.Contains("10GiB") ? 5 : 10;
                string path = Path.Combine(Paths.Corpus, name);
                using var mf = MappedFile.Open(path);
                foreach (var p in FilePatterns)
                    await Compare(csv, name, p, reps,
                        () => baseline(p, path),
                        engines: p.Regex
                            ? [("core-compiled", () => CoreRegex(mf, p, RegexOptions.Compiled)), ("core-nonbacktracking", () => CoreRegex(mf, p, RegexOptions.NonBacktracking))]
                            : [("core", () => CoreLiteral(mf, p))]);
                if (!count) await Cancel(csv, name, FilePatterns[1], FilePatterns[2], mf, path, 150);
            }
        }
        else
        {
            string root = Path.Combine(Paths.Corpus, "tree");
            foreach (var p in TreePatterns)
                await Compare(csv, "tree", p, 5,
                    () => baseline(p, root),
                    engines: p.Regex
                        ? [("core-compiled", () => TreeRegex(root, p, RegexOptions.Compiled)), ("core-nonbacktracking", () => TreeRegex(root, p, RegexOptions.NonBacktracking))]
                        : [("core", () => TreeLiteral(root, p))]);
            if (!count) await CancelTree(csv, root, TreePatterns[1], 100);
        }
    }

    private static async Task Compare(Csv csv, string target, Pat p, int reps, Func<Task<(double Sec, long Matches)>> rg, (string Name, Func<Task<(double Sec, long Matches)>> Run)[] engines)
    {
        Console.WriteLine($"-- {target} / {p.Label} '{p.Text}' reps={reps}");
        var all = new List<(string Name, Func<Task<(double, long)>> Run)> { (BaselineName, rg) };
        all.AddRange(engines.Select(e => (e.Name, e.Run)));
        var samples = all.ToDictionary(e => e.Name, _ => new List<double>());
        var counts = new Dictionary<string, long>();
        foreach (var e in all) await e.Run();
        for (int i = 0; i < reps; i++)
        {
            foreach (var e in all)
            {
                var (sec, matches) = await e.Run();
                samples[e.Name].Add(sec);
                counts[e.Name] = matches;
                csv.Row("A5", target, p.Label, e.Name, i, sec, "s", matches);
            }
        }
        double rgMed = Stats.Percentile(samples[BaselineName], .5);
        foreach (var e in all)
        {
            double med = Stats.Percentile(samples[e.Name], .5);
            Console.WriteLine($"{Stats.Summary(e.Name, samples[e.Name], "s")} matches={counts[e.Name]} ratio-to-baseline(median)={med / rgMed:F2}  count {(counts[e.Name] == counts[BaselineName] ? "== baseline" : "!= baseline MISMATCH")}");
        }
    }

    private static async Task<(double, long)> CoreLiteral(MappedFile mf, Pat p)
    {
        long t0 = Stopwatch.GetTimestamp();
        var r = await PagedSearch.SearchLiteralAsync(mf, Encoding.UTF8.GetBytes(p.Text));
        return (Sec(t0), r.MatchCount);
    }

    private static async Task<(double, long)> CoreRegex(MappedFile mf, Pat p, RegexOptions o)
    {
        var rx = new Regex(p.Text, o | RegexOptions.CultureInvariant);
        long t0 = Stopwatch.GetTimestamp();
        var r = await PagedSearch.SearchRegexAsync(mf, Encoding.UTF8, rx);
        return (Sec(t0), r.MatchCount);
    }

    private static async Task<(double, long)> TreeLiteral(string root, Pat p)
    {
        long t0 = Stopwatch.GetTimestamp();
        var r = await TreeSearch.SearchLiteralAsync(root, Encoding.UTF8.GetBytes(p.Text));
        return (Sec(t0), r.Matches);
    }

    private static async Task<(double, long)> TreeRegex(string root, Pat p, RegexOptions o)
    {
        var rx = new Regex(p.Text, o | RegexOptions.CultureInvariant);
        long t0 = Stopwatch.GetTimestamp();
        var r = await TreeSearch.SearchRegexAsync(root, rx);
        return (Sec(t0), r.Matches);
    }

    private static ProcessStartInfo RgInfo(Pat p, string target)
    {
        var psi = new ProcessStartInfo(Paths.Rg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "--json", "--no-config" }) psi.ArgumentList.Add(a);
        if (!p.Regex) psi.ArgumentList.Add("-F");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(p.Text);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(target);
        return psi;
    }

    /// <summary>Runs rg --json, discards the stream except its tail and reads <c>stats.matches</c> from the summary message.</summary>
    private static async Task<(double, long)> RunRg(Pat p, string target)
    {
        long t0 = Stopwatch.GetTimestamp();
        using var proc = Process.Start(RgInfo(p, target))!;
        var err = proc.StandardError.ReadToEndAsync();
        var tail = new byte[16384];
        int tailLen = 0;
        var buf = new byte[1 << 20];
        var stream = proc.StandardOutput.BaseStream;
        int n;
        while ((n = await stream.ReadAsync(buf)) > 0)
        {
            if (n >= tail.Length) { Buffer.BlockCopy(buf, n - tail.Length, tail, 0, tail.Length); tailLen = tail.Length; }
            else
            {
                int keep = Math.Min(tailLen, tail.Length - n);
                Buffer.BlockCopy(tail, tailLen - keep, tail, 0, keep);
                Buffer.BlockCopy(buf, 0, tail, keep, n);
                tailLen = keep + n;
            }
        }
        await proc.WaitForExitAsync();
        double sec = Sec(t0);
        string text = Encoding.UTF8.GetString(tail, 0, tailLen);
        string? last = text.Split('\n').LastOrDefault(l => l.StartsWith('{') && l.TrimEnd().EndsWith('}'));
        if (last is null || !last.Contains("\"type\":\"summary\"")) throw new InvalidOperationException("rg summary not found: " + await err);
        using var doc = JsonDocument.Parse(last);
        return (sec, doc.RootElement.GetProperty("data").GetProperty("stats").GetProperty("matches").GetInt64());
    }

    /// <summary>Runs rg --count-matches (no JSON) and sums the per-file counts.</summary>
    private static async Task<(double, long)> RunRgCount(Pat p, string target)
    {
        var psi = RgInfo(p, target);
        psi.ArgumentList.Remove("--json");
        psi.ArgumentList.Insert(0, "--count-matches");
        long t0 = Stopwatch.GetTimestamp();
        using var proc = Process.Start(psi)!;
        var err = proc.StandardError.ReadToEndAsync();
        string text = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        double sec = Sec(t0);
        long total = 0;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            total += long.Parse(line[(line.LastIndexOf(':') + 1)..].Trim());
        return (sec, total);
    }

    private static async Task Cancel(Csv csv, string name, Pat literal, Pat regex, MappedFile mf, string path, int delayMs)
    {
        Console.WriteLine($"-- cancel on {name}: cancel {delayMs} ms after start, time from cancel request to stop");
        var coreLit = new List<double>(); var coreRx = new List<double>(); var rg = new List<double>();
        int finishedEarly = 0;
        var rxCompiled = new Regex(regex.Text, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        for (int i = 0; i < 30; i++)
        {
            foreach (var kind in new[] { "lit", "rx" })
            {
                using var cts = new CancellationTokenSource();
                var task = kind == "lit"
                    ? PagedSearch.SearchLiteralAsync(mf, Encoding.UTF8.GetBytes(literal.Text), null, cts.Token)
                    : PagedSearch.SearchRegexAsync(mf, Encoding.UTF8, rxCompiled, null, cts.Token);
                await Task.Delay(delayMs);
                long t0 = Stopwatch.GetTimestamp();
                cts.Cancel();
                try { await task; finishedEarly++; continue; } catch (OperationCanceledException) { }
                double ms = Sec(t0) * 1000;
                (kind == "lit" ? coreLit : coreRx).Add(ms);
                csv.Row("A5", name, kind == "lit" ? "frequent-literal" : "regex", "core-cancel", i, ms, "ms", 0);
            }

            using var proc = Process.Start(RgInfo(literal, path))!;
            var drain = proc.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            await Task.Delay(delayMs);
            if (proc.HasExited) { finishedEarly++; continue; }
            long t1 = Stopwatch.GetTimestamp();
            proc.Kill(true);
            await proc.WaitForExitAsync();
            double rms = Sec(t1) * 1000;
            rg.Add(rms);
            csv.Row("A5", name, "frequent-literal", "rg-cancel", i, rms, "ms", 0);
        }
        Console.WriteLine($"(searches that finished before the cancel: {finishedEarly})");
        if (coreLit.Count > 0) Console.WriteLine(Stats.Summary("core literal cancel", coreLit, "ms"));
        if (coreRx.Count > 0) Console.WriteLine(Stats.Summary("core regex cancel", coreRx, "ms"));
        if (rg.Count > 0) Console.WriteLine(Stats.Summary("rg kill", rg, "ms"));
    }

    private static async Task CancelTree(Csv csv, string root, Pat literal, int delayMs)
    {
        Console.WriteLine($"-- cancel on tree: cancel {delayMs} ms after start");
        var core = new List<double>(); var rg = new List<double>();
        int finishedEarly = 0;
        for (int i = 0; i < 30; i++)
        {
            using var cts = new CancellationTokenSource();
            var task = TreeSearch.SearchLiteralAsync(root, Encoding.UTF8.GetBytes(literal.Text), 0, null, cts.Token);
            await Task.Delay(delayMs);
            long t0 = Stopwatch.GetTimestamp();
            cts.Cancel();
            try { await task; finishedEarly++; }
            catch (OperationCanceledException)
            {
                core.Add(Sec(t0) * 1000);
                csv.Row("A5", "tree", "frequent-literal", "core-cancel", i, core[^1], "ms", 0);
            }

            using var proc = Process.Start(RgInfo(literal, root))!;
            var drain = proc.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            await Task.Delay(delayMs);
            if (proc.HasExited) { finishedEarly++; continue; }
            long t1 = Stopwatch.GetTimestamp();
            proc.Kill(true);
            await proc.WaitForExitAsync();
            rg.Add(Sec(t1) * 1000);
            csv.Row("A5", "tree", "frequent-literal", "rg-cancel", i, rg[^1], "ms", 0);
        }
        Console.WriteLine($"(finished before cancel: {finishedEarly})");
        if (core.Count > 0) Console.WriteLine(Stats.Summary("core tree cancel", core, "ms"));
        if (rg.Count > 0) Console.WriteLine(Stats.Summary("rg tree kill", rg, "ms"));
    }
}

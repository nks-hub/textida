using System.Diagnostics;
using System.Globalization;

namespace NksPad.Bench;

/// <summary>
/// Scenario A7: launches the UI app with <c>--bench-startup[=minimal]</c> repeatedly and reads the
/// <c>STARTUP_MS=n</c> line (process start to the second animation frame). Private bytes are sampled
/// from outside every 5 ms until the process exits; the last sample approximates the value at first frame.
/// </summary>
internal static class A7Startup
{
    public static async Task Run(string[] args)
    {
        string exe = args[1];
        string label = args[2];
        string mode = Args.Opt(args, "--mode") ?? "--bench-startup";
        int n = int.Parse(Args.Opt(args, "--n") ?? "30");
        int warmup = int.Parse(Args.Opt(args, "--warmup") ?? "2");
        using var csv = new Csv("a7-startup.csv", "scenario,variant,mode,rep,startup_ms,wall_ms,private_bytes,working_set_bytes");

        var startup = new List<double>(); var wall = new List<double>(); var priv = new List<double>(); var ws = new List<double>();
        for (int i = -warmup; i < n; i++)
        {
            var psi = new ProcessStartInfo(exe, mode) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = false };
            long t0 = Stopwatch.GetTimestamp();
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            long lastPrivate = 0, lastWs = 0;
            while (!p.HasExited)
            {
                try
                {
                    p.Refresh();
                    lastPrivate = p.PrivateMemorySize64;
                    lastWs = p.WorkingSet64;
                }
                catch (InvalidOperationException) { break; }
                await Task.Delay(5);
            }
            await p.WaitForExitAsync();
            double wallMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            string text = await stdout;
            string err = await stderr;
            var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("STARTUP_MS=", StringComparison.Ordinal));
            if (line is null)
            {
                Console.WriteLine($"run {i}: no STARTUP_MS line (exit {p.ExitCode}); stdout='{text.Trim()}' stderr='{err.Trim()[..Math.Min(300, err.Trim().Length)]}'");
                if (i >= 0) Environment.ExitCode = 1;
                continue;
            }
            double ms = double.Parse(line["STARTUP_MS=".Length..], CultureInfo.InvariantCulture);
            if (i < 0) continue;
            startup.Add(ms); wall.Add(wallMs); priv.Add(lastPrivate); ws.Add(lastWs);
            csv.Row("A7", label, mode, i, ms, wallMs, lastPrivate, lastWs);
        }

        if (startup.Count == 0) { Console.WriteLine($"{label}: no successful runs"); return; }
        Console.WriteLine(Stats.Summary($"{label} {mode} STARTUP_MS", startup, "ms"));
        Console.WriteLine(Stats.Summary($"{label} {mode} wall", wall, "ms"));
        Console.WriteLine($"{label} {mode} private bytes (last sample): median={Mem.Mib((long)Stats.Percentile(priv, .5))} MiB p95={Mem.Mib((long)Stats.Percentile(priv, .95))} MiB; working set median={Mem.Mib((long)Stats.Percentile(ws, .5))} MiB");
    }
}

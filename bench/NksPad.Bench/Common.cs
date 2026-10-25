using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NksPad.Bench;

/// <summary>Deterministic splitmix64 generator so corpus files are reproducible.</summary>
internal sealed class Rng(ulong seed)
{
    private ulong _s = seed;

    public ulong Next()
    {
        ulong z = _s += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int n) => (int)((Next() >> 33) % (ulong)n);
}

internal static class Paths
{
    public static string Root { get; private set; } = OperatingSystem.IsWindows() ? @"C:\nkspad-bench" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "nkspad");
    public static string Corpus { get; private set; } = Path.Combine(Root, "corpus");
    public static string Results { get; private set; } = Path.Combine(Root, "results");
    public static string Manifest => Path.Combine(Corpus, "manifest");
    public static string Scratch { get; private set; } = Path.Combine(Root, "scratch");
    /// <summary>Corpus size profile: "full" (up to 50 GiB, tree ~10 GiB) or "mac" (up to 10 GiB, tree ~2 GiB).</summary>
    public static string Profile { get; private set; } = "full";
    public static string Rg { get; private set; } = "rg";

    /// <summary>Applies <c>--corpus</c>, <c>--results</c>, <c>--scratch</c>, <c>--profile</c> and <c>--rg</c> from the command line.</summary>
    public static void Configure(string[] args)
    {
        Corpus = Args.Opt(args, "--corpus") ?? Corpus;
        Results = Args.Opt(args, "--results") ?? Results;
        Scratch = Args.Opt(args, "--scratch") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Corpus))!, "scratch");
        Profile = Args.Opt(args, "--profile") ?? Profile;
        Rg = Args.Opt(args, "--rg") ?? Rg;
    }
}

internal static class Stats
{
    public static double Percentile(IReadOnlyList<double> samples, double p)
    {
        if (samples.Count == 0) return double.NaN;
        var s = samples.OrderBy(x => x).ToArray();
        double rank = p * (s.Length - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return s[lo] + (s[hi] - s[lo]) * (rank - lo);
    }

    public static string Summary(string label, IReadOnlyList<double> samples, string unit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{label}: n={samples.Count} median={Percentile(samples, .5):F3}{unit} p95={Percentile(samples, .95):F3}{unit} max={samples.Max():F3}{unit}");
}

/// <summary>Appends raw samples and summary rows to CSV files in the results directory.</summary>
internal sealed class Csv : IDisposable
{
    private readonly StreamWriter _w;

    public Csv(string name, string header)
    {
        Directory.CreateDirectory(Paths.Results);
        bool exists = File.Exists(Path.Combine(Paths.Results, name));
        _w = new StreamWriter(new FileStream(Path.Combine(Paths.Results, name), FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        if (!exists) _w.WriteLine(header);
    }

    public void Row(params object?[] cells) =>
        _w.WriteLine(string.Join(',', cells.Select(c => Convert.ToString(c, CultureInfo.InvariantCulture)?.Replace(',', ';'))));

    public void Dispose() => _w.Dispose();
}

internal static class Mem
{
    public static (long PrivateBytes, long WorkingSet, long PeakWorkingSet, long PeakCommit) Snapshot()
    {
        using var p = Process.GetCurrentProcess();
        long peakCommit = 0;
        try { peakCommit = p.PeakPagedMemorySize64; } catch (PlatformNotSupportedException) { }
        return (p.PrivateMemorySize64, p.WorkingSet64, p.PeakWorkingSet64, peakCommit);
    }

    public static string Mib(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture);
}

internal static class Args
{
    public static string? Opt(string[] a, string name) =>
        a.SkipWhile(x => x != name).Skip(1).FirstOrDefault();

    public static bool Flag(string[] a, string name) => a.Contains(name);
}

internal static class Hashing
{
    public static string Sha256File(string path)
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var buf = new byte[8 << 20];
        int n;
        while ((n = fs.Read(buf)) > 0) h.AppendData(buf, 0, n);
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }
}

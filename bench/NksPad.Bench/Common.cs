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
    public const string Root = @"C:\nkspad-bench";
    public static readonly string Corpus = Path.Combine(Root, "corpus");
    public static readonly string Results = Path.Combine(Root, "results");
    public static readonly string Manifest = Path.Combine(Corpus, "manifest");
    public static readonly string Scratch = Path.Combine(Root, "scratch");
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
        return (p.PrivateMemorySize64, p.WorkingSet64, p.PeakWorkingSet64, p.PeakPagedMemorySize64);
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

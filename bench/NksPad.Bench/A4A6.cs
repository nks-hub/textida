using System.Diagnostics;
using System.Text;
using System.Text.Unicode;
using NksPad.Core;

namespace NksPad.Bench;

/// <summary>Scenario A4: slice of a 100 MiB single line without materializing it.</summary>
internal static class A4LongLine
{
    public static async Task Run(string[] args)
    {
        string path = Path.Combine(Paths.Corpus, "oneline-100MiB.txt");
        using var csv = new Csv("a4-longline.csv", "scenario,metric,rep,value,unit");
        var m0 = Mem.Snapshot();
        using var src = MappedFileSource.Open(path);
        await src.IndexTask;
        Console.WriteLine($"opened {src.ByteLength} B, lines={src.LineIndexState}, private={Mem.Mib(Mem.Snapshot().PrivateBytes)} MiB (was {Mem.Mib(m0.PrivateBytes)})");

        foreach (long col in new long[] { 0, 50_000_000, 90_000_000 })
        {
            var times = new List<double>();
            string? slice = null;
            for (int i = 0; i < 30; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                slice = await src.ReadColumnsAsync(0, col + i * 4096, 200);
                times.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
            for (int i = 0; i < times.Count; i++) csv.Row("A4", $"slice-col{col}", i, times[i], "ms");
            Console.WriteLine(Stats.Summary($"slice at column {col} (200 cols), first sample {times[0]:F2} ms", times, "ms"));
            string expected = ReferenceSlice(path, col + 29 * 4096, 200);
            Console.WriteLine($"  content check vs. streaming reference: {(slice == expected ? "OK" : "MISMATCH")} ('{slice![..Math.Min(40, slice.Length)]}...')");
            if (slice != expected) Environment.ExitCode = 1;
        }

        var end = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            long e = src.FindLineEnd(0);
            end.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            if (i == 0) Console.WriteLine($"FindLineEnd(0) = {e}");
        }
        Console.WriteLine(Stats.Summary("FindLineEnd over 100 MiB line", end, "ms"));
        var m1 = Mem.Snapshot();
        Console.WriteLine($"after slices: private={Mem.Mib(m1.PrivateBytes)} MiB (delta {Mem.Mib(m1.PrivateBytes - m0.PrivateBytes)} MiB), workingSet={Mem.Mib(m1.WorkingSet)} MiB, peakWS={Mem.Mib(m1.PeakWorkingSet)} MiB");
        csv.Row("A4", "private-delta", 0, m1.PrivateBytes - m0.PrivateBytes, "B");

        long tm = Stopwatch.GetTimestamp();
        var whole = src.ReadLines(0, 1);
        Console.WriteLine($"for contrast, materializing the line: {(Stopwatch.GetTimestamp() - tm) * 1000.0 / Stopwatch.Frequency:F1} ms, {whole[0].Length} chars, private={Mem.Mib(Mem.Snapshot().PrivateBytes)} MiB");
    }

    private static string ReferenceSlice(string path, long startCol, int count)
    {
        using var fs = File.OpenRead(path);
        var dec = Encoding.UTF8.GetDecoder();
        var bytes = new byte[1 << 20];
        var chars = new char[(1 << 20) + 8];
        var sb = new StringBuilder();
        long seen = 0;
        int n;
        while ((n = fs.Read(bytes)) > 0 && sb.Length < count)
        {
            int c = dec.GetChars(bytes, 0, n, chars, 0);
            long from = Math.Max(0, startCol - seen);
            if (from < c) sb.Append(chars, (int)from, (int)Math.Min(c - from, count - sb.Length));
            seen += c;
        }
        return sb.ToString();
    }
}

/// <summary>Scenario A6: UTF-8 validation, detection and decoding throughput.</summary>
internal static class A6Encoding
{
    private static double Sec(long t0) => (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;

    public static void Run(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var csv = new Csv("a6-encoding.csv", "scenario,metric,rep,value,unit");
        void Report(string metric, List<double> v, string unit)
        {
            for (int i = 0; i < v.Count; i++) csv.Row("A6", metric, i, v[i], unit);
            Console.WriteLine(Stats.Summary(metric, v, unit));
        }

        foreach (var name in new[] { "text-10MiB.log", "text-1GiB.log", "cp1250-10MiB.txt", "utf16le-100MiB.txt", "oneline-100MiB.txt", "json-100MiB.json", "csv-1GiB.csv" })
        {
            using var f = MappedFile.Open(Path.Combine(Paths.Corpus, name));
            var d = EncodingDetector.Detect(f.GetSpanClamped(0, 1 << 20), f.Length <= 1 << 20);
            Console.WriteLine($"detect {name}: {d.Kind}, {d.Encoding.WebName}, bom={d.BomLength}");
        }

        string big = Path.Combine(Paths.Corpus, "text-1GiB.log");
        using (var f = MappedFile.Open(big))
        {
            var detect = new List<double>();
            for (int i = 0; i < 30; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                EncodingDetector.Detect(f.GetSpanClamped(0, 1 << 20), false);
                detect.Add(Sec(t0) * 1000);
            }
            Report("detect-1MiB-sample", detect, "ms");

            var single = new List<double>(); var par = new List<double>(); var dec = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                bool ok = true;
                long pos = 0;
                while (pos < f.Length)
                {
                    var span = f.GetSpanClamped(pos, 8 << 20);
                    int len = pos + span.Length == f.Length ? span.Length : Utf8Validator.CompleteLength(span);
                    ok &= Utf8.IsValid(span[..len]);
                    pos += len;
                }
                single.Add(f.Length / Sec(t0) / 1e9);
                if (!ok) throw new InvalidOperationException("corpus is not valid UTF-8");

                t0 = Stopwatch.GetTimestamp();
                if (!Utf8Validator.IsValid(f)) throw new InvalidOperationException("corpus is not valid UTF-8");
                par.Add(f.Length / Sec(t0) / 1e9);

                t0 = Stopwatch.GetTimestamp();
                var chars = new char[(8 << 20) + 4];
                pos = 0;
                long total = 0;
                while (pos < f.Length)
                {
                    var span = f.GetSpanClamped(pos, 8 << 20);
                    int len = pos + span.Length == f.Length ? span.Length : Utf8Validator.CompleteLength(span);
                    total += Encoding.UTF8.GetChars(span[..len], chars);
                    pos += len;
                }
                dec.Add(f.Length / Sec(t0) / 1e9);
                if (i == 0) Console.WriteLine($"decoded chars: {total}");
            }
            Report("utf8-validate-1GiB-single-thread", single, "GB/s");
            Report("utf8-validate-1GiB-parallel", par, "GB/s");
            Report("utf8-decode-1GiB-single-thread", dec, "GB/s");
        }

        var cp = Encoding.GetEncoding(1250);
        byte[] cpBytes = File.ReadAllBytes(Path.Combine(Paths.Corpus, "cp1250-10MiB.txt"));
        var cpT = new List<double>();
        string cpText = "";
        for (int i = 0; i < 30; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            cpText = cp.GetString(cpBytes);
            cpT.Add(cpBytes.Length / Sec(t0) / 1e6);
        }
        Report("cp1250-decode-10MiB", cpT, "MB/s");
        Console.WriteLine($"cp1250 round trip identical: {cp.GetBytes(cpText).AsSpan().SequenceEqual(cpBytes)}; sample: {cpText[..Math.Min(0, cpText.Length)]}{cpText.Split('\n')[5]}");

        byte[] u16 = File.ReadAllBytes(Path.Combine(Paths.Corpus, "utf16le-100MiB.txt"));
        var u16T = new List<double>();
        string u16Text = "";
        var enc = new UnicodeEncoding(false, false);
        for (int i = 0; i < 10; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            u16Text = enc.GetString(u16, 2, u16.Length - 2);
            u16T.Add(u16.Length / Sec(t0) / 1e6);
        }
        Report("utf16le-decode-100MiB", u16T, "MB/s");
        var back = new byte[u16.Length];
        back[0] = 0xFF; back[1] = 0xFE;
        enc.GetBytes(u16Text, 0, u16Text.Length, back, 2);
        Console.WriteLine($"utf16 round trip identical: {back.AsSpan().SequenceEqual(u16)}; chars={u16Text.Length}; sample: {u16Text.Split('\n')[3]}");
    }
}

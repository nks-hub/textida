using System.Diagnostics;
using NksPad.Core;

namespace NksPad.Bench;

/// <summary>Diagnostic: sequential copy strategies from a mapped file to a new file, to find what limits streaming save.</summary>
internal static class WriteTest
{
    public static void Run(string[] args)
    {
        string name = Corpus.PositionalArgs(args)[0];
        string src = Path.Combine(Paths.Corpus, name);
        Directory.CreateDirectory(Paths.Scratch);
        string dst = Path.Combine(Paths.Scratch, "writetest.bin");
        using var mf = MappedFile.Open(src);
        long len = mf.Length;

        void Time(string label, Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            double s = sw.Elapsed.TotalSeconds;
            Console.WriteLine($"{label}: {s:F2}s, {len / s / 1e9:F2} GB/s");
            File.Delete(dst);
        }

        Time("File.Copy", () => File.Copy(src, dst, true));
        foreach (int slice in new[] { 1 << 20, 8 << 20, 64 << 20 })
        {
            Time($"FileStream no prealloc, slice {slice >> 20} MiB, from mapping", () =>
            {
                using var fs = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0, FileOptions.SequentialScan);
                for (long o = 0; o < len; o += slice) fs.Write(mf.GetSpan(o, (int)Math.Min(slice, len - o)));
            });
        }
        Time("FileStream prealloc, slice 8 MiB", () =>
        {
            using var fs = new FileStream(dst, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, PreallocationSize = len, BufferSize = 0 });
            for (long o = 0; o < len; o += 8 << 20) fs.Write(mf.GetSpan(o, (int)Math.Min(8 << 20, len - o)));
        });
        foreach (int threads in new[] { 2, 4, 8 })
        {
            Time($"RandomAccess.Write, {threads} parallel streams of 8 MiB slices", () =>
            {
                using var h = File.OpenHandle(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.None, len);
                long slices = (len + (8 << 20) - 1) / (8 << 20);
                Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
                {
                    for (long i = t; i < slices; i += threads)
                    {
                        long o = i * (8 << 20);
                        RandomAccess.Write(h, mf.GetSpan(o, (int)Math.Min(8 << 20, len - o)), o);
                    }
                });
            });
        }
        Time("FileStream no prealloc + flush(true) at end, slice 8 MiB", () =>
        {
            using var fs = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0, FileOptions.SequentialScan);
            for (long o = 0; o < len; o += 8 << 20) fs.Write(mf.GetSpan(o, (int)Math.Min(8 << 20, len - o)));
            fs.Flush(true);
        });
    }
}

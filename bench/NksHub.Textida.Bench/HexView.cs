using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core;

namespace NksHub.Textida.Bench;

/// <summary>
/// Hex scenario: viewport read + format, byte-pattern search with wildcards, one-byte overwrite/insert and a
/// byte-exact save, all through <see cref="PagedDocument"/>. The wildcard search is a bench prototype, the core has none.
/// </summary>
internal static class HexView
{
    private const int Rows = 64, PerRow = 16;
    private static double Us(long ticks) => ticks * 1e6 / Stopwatch.Frequency;

    public static async Task Run(string[] args)
    {
        string name = args[1];
        string path = Path.Combine(Paths.Corpus, name);
        int samples = int.Parse(Args.Opt(args, "--samples") ?? "2000");
        int reps = int.Parse(Args.Opt(args, "--reps") ?? "5");
        bool save = Args.Flag(args, "--save");
        long length = new FileInfo(path).Length;
        using var csv = new Csv($"hex-{name}.csv", "scenario,metric,rep,value,unit");
        void Report(string metric, List<double> v, string unit)
        {
            for (int i = 0; i < v.Count; i++) csv.Row("HEX", metric, i, v[i], unit);
            Console.WriteLine(Summary(metric, v, unit));
        }

        Console.WriteLine($"== HEX {name}: {length} B ({length / 1073741824.0:F2} GiB), samples={samples}, reps={reps}, cpu={Environment.ProcessorCount}");
        var ascii = Table(null);
        var cp1250 = Table(Encoding.GetEncoding(1250));

        // (a) viewport: 64 rows x 16 B at random 16-aligned offsets, read through the piece tree and formatted to row strings
        using (var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            var buf = new byte[Rows * PerRow];
            var line = new char[128];
            int digits = OffsetDigits(length);
            long t0 = Stopwatch.GetTimestamp();
            var rows = Viewport(doc, 0, buf, line, digits, ascii);
            Console.WriteLine($"first viewport in process (cold JIT): {Us(Stopwatch.GetTimestamp() - t0):F0} us");
            Console.WriteLine(rows[0]);
            Console.WriteLine(Viewport(doc, length / 2 & ~15L, buf, line, digits, cp1250)[0]);

            // "untouched": pages never touched by this process and possibly not in the file cache (cache state unknown);
            // "warm": file read sequentially just before, pages not yet mapped in this process; "mapped": same offsets again
            var rnd = new Random(42);
            long[] Offsets() => Enumerable.Range(0, samples).Select(_ => rnd.NextInt64(0, length - buf.Length) & ~15L).ToArray();
            var untouched = Offsets();
            Report("viewport-untouched-ascii", Measure(untouched, o => Viewport(doc, o, buf, line, digits, ascii)), "us");
            var sw = Stopwatch.StartNew();
            long seq = SequentialRead(path);
            Console.WriteLine($"sequential read of the whole file (warms the cache): {sw.Elapsed.TotalSeconds:F2}s, {seq / sw.Elapsed.TotalSeconds / 1e9:F2} GB/s");
            var warm = Offsets();
            Report("viewport-warm-ascii", Measure(warm, o => Viewport(doc, o, buf, line, digits, ascii)), "us");
            Report("viewport-mapped-ascii", Measure(warm, o => Viewport(doc, o, buf, line, digits, ascii)), "us");
            Report("viewport-mapped-cp1250", Measure(warm, o => Viewport(doc, o, buf, line, digits, cp1250)), "us");
            Report("read-only-mapped", Measure(warm, o => doc.Read(o, buf)), "us");
            var offsets = warm;
            long top = length / 3 & ~15L;
            var scroll = Enumerable.Range(0, samples).Select(i => top + (long)i * buf.Length).ToArray();
            Report("viewport-page-down", Measure(scroll, o => Viewport(doc, o, buf, line, digits, ascii)), "us");

            // the same after 1000 overwrites and 1000 inserts scattered over the file
            var er = new Random(9);
            for (int i = 0; i < 1000; i++)
            {
                doc.Replace(er.NextInt64(0, doc.ByteLength - 1), 1, [0xAA]);
                doc.Insert(er.NextInt64(0, doc.ByteLength), [0x55]);
            }
            Console.WriteLine($"edited document: pieces={doc.PieceCount}");
            Report("viewport-random-edited", Measure(offsets, o => Viewport(doc, o, buf, line, digits, ascii)), "us");
        }
        if (Args.Flag(args, "--view-only")) return;

        // (b) search: core literal (original file only, no CR/LF) vs the masked prototype over the file and over the edited tree
        using (var file = MappedFile.Open(path))
        {
            var cases = new (string Label, string Hex)[]
            {
                ("rare-8B", "4E 4B 53 50 41 44 5F 52"),          // TEXTIDA_R
                ("rare-wild-6B", "4E 4B ?? 50 41 44"),           // NK?PAD
                ("frequent-7B", "74 69 6D 65 6F 75 74"),         // timeout
                ("frequent-wild-6B", "73 3D 35 ?? ?? 20"),       // s=5?? (status=5xx)
                ("nibble-wild-5B", "3D 3? 3? 3? 6D"),            // =ddd m, nibble wildcards
                ("with-LF-5B", "0A 32 30 32 36"),                // \n2026, rejected by the core literal search
            };
            foreach (var (label, hex) in cases)
            {
                var p = BytePattern.Parse(hex);
                long check = Naive(file.GetSpan(0, 256 << 20), p);
                long proto256 = await CountFile(file, p, 256L << 20);
                Console.WriteLine($"self-check {label} on first 256 MiB: naive={check} prototype={proto256} {(check == proto256 ? "OK" : "MISMATCH")}");
                if (check != proto256) Environment.ExitCode = 1;

                var times = new List<double>();
                long n = 0;
                await CountFile(file, p, file.Length);
                for (int r = 0; r < reps; r++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    n = await CountFile(file, p, file.Length);
                    times.Add((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
                }
                Report($"search-proto-{label}", times, "s");
                Console.WriteLine($"  matches={n}, {file.Length / Percentile(times, .5) / 1e9:F1} GB/s");

                if (!p.HasWildcard)
                {
                    try
                    {
                        var core = new List<double>();
                        SearchResult res = await PagedSearch.SearchLiteralAsync(file, p.Value);
                        for (int r = 0; r < reps; r++)
                        {
                            long t0 = Stopwatch.GetTimestamp();
                            res = await PagedSearch.SearchLiteralAsync(file, p.Value);
                            core.Add((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
                        }
                        Report($"search-core-{label}", core, "s");
                        Console.WriteLine($"  core matches={res.MatchCount} {(res.MatchCount == n ? "OK same as prototype" : "MISMATCH")}");
                        if (res.MatchCount != n) Environment.ExitCode = 1;
                    }
                    catch (ArgumentException e) { Console.WriteLine($"  core literal search rejects the pattern: {e.Message}"); }
                }
            }

            // the edited document: the search must read the piece tree, not the file
            using var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false });
            var er = new Random(11);
            for (int i = 0; i < 1000; i++) doc.Insert(er.NextInt64(0, doc.ByteLength), [0x55]);
            var pr = BytePattern.Parse("4E 4B 53 50 41 44 5F 52");
            long expect = await CountFile(file, pr, file.Length);
            var dt = new List<double>();
            long dn = 0;
            for (int r = 0; r < reps; r++)
            {
                long t0 = Stopwatch.GetTimestamp();
                dn = await CountDocument(doc, pr);
                dt.Add((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
            }
            Report("search-proto-edited-tree-rare-8B", dt, "s");
            // 0x55 ('U') cannot form the pattern; a split occurrence would need an insert inside one of 320 x 8 bytes
            Console.WriteLine($"  matches={dn} file={expect} {(dn == expect ? "OK" : "DIFF")} pieces={doc.PieceCount}");
        }

        // (c) one-byte overwrite and insert in the middle, undo, then a byte-exact save
        using (var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            long mid = length / 2;
            long t0 = Stopwatch.GetTimestamp();
            doc.Replace(mid, 1, [0x41]);
            double firstOver = Us(Stopwatch.GetTimestamp() - t0);
            doc.Undo();
            Console.WriteLine($"first overwrite in process: {firstOver:F1} us");
            var rnd = new Random(5);
            var over = new List<double>(); var ins = new List<double>(); var undo = new List<double>();
            for (int i = 0; i < 1000; i++)
            {
                long o = mid + rnd.NextInt64(-length / 4, length / 4);
                t0 = Stopwatch.GetTimestamp();
                doc.Replace(o, 1, [(byte)i]);
                over.Add(Us(Stopwatch.GetTimestamp() - t0));
                t0 = Stopwatch.GetTimestamp();
                doc.Insert(o + 7, [(byte)i]);
                ins.Add(Us(Stopwatch.GetTimestamp() - t0));
            }
            for (int i = 0; i < 2000; i++)
            {
                t0 = Stopwatch.GetTimestamp();
                doc.Undo();
                undo.Add(Us(Stopwatch.GetTimestamp() - t0));
            }
            Report("overwrite-1B", over, "us");
            Report("insert-1B", ins, "us");
            Report("undo-1B", undo, "us");
            Console.WriteLine($"after undo all: length equals original={doc.ByteLength == length}, pieces={doc.PieceCount}");
        }

        // (d) hashes of a 1 GiB selection and the entropy map for the minimap
        using (var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false }))
        {
            long from = length / 4, len = Math.Min(1L << 30, length - from);
            foreach (var alg in new[] { HashAlgorithmName.MD5, HashAlgorithmName.SHA1, HashAlgorithmName.SHA256 })
            {
                var t = new List<double>();
                for (int r = 0; r < Math.Min(reps, 3); r++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    HashRange(doc, from, len, alg);
                    t.Add((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
                }
                Report($"hash-1GiB-{alg.Name}", t, "s");
                Console.WriteLine($"  {len / Percentile(t, .5) / 1e9:F2} GB/s");
            }
            foreach (int buckets in new[] { 1024, 4096 })
            {
                var t = new List<double>();
                double[] map = [];
                for (int r = 0; r < reps; r++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    map = SampledEntropy(doc, buckets, 4096);
                    t.Add(Us(Stopwatch.GetTimestamp() - t0) / 1000);
                }
                Report($"entropy-map-sampled-{buckets}x4KiB", t, "ms");
                Console.WriteLine($"  entropy min={map.Min():F2} max={map.Max():F2} bits/B");
            }
            using var file = MappedFile.Open(path);
            var ft = new List<double>();
            for (int r = 0; r < Math.Min(reps, 3); r++)
            {
                long t0 = Stopwatch.GetTimestamp();
                FullEntropy(file, 64 << 10);
                ft.Add((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency);
            }
            Report("entropy-full-64KiB-blocks", ft, "s");
            Console.WriteLine($"  {length / Percentile(ft, .5) / 1e9:F1} GB/s");
        }

        if (save) await SaveScenario(path, name, length, reps, Report);
        Console.WriteLine($"final: {NksHub.Textida.Bench.Mem.Snapshot()}");
    }

    private static List<double> Measure(long[] offsets, Action<long> act)
    {
        var v = new List<double>(offsets.Length);
        foreach (var o in offsets)
        {
            long t0 = Stopwatch.GetTimestamp();
            act(o);
            v.Add(Us(Stopwatch.GetTimestamp() - t0));
        }
        return v;
    }

    private static string Summary(string label, List<double> v, string unit) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{label}: n={v.Count} p50={Percentile(v, .5):F3}{unit} p95={Percentile(v, .95):F3}{unit} p99={Percentile(v, .99):F3}{unit} max={v.Max():F3}{unit}");

    private static double Percentile(List<double> v, double p) => Stats.Percentile(v, p);

    private static int OffsetDigits(long length) => Math.Max(8, (64 - (int)long.LeadingZeroCount(Math.Max(1, length - 1)) + 3) / 4);

    /// <summary>Byte to glyph for the text column: printable characters of the encoding, '.' for control and unmapped bytes.</summary>
    private static char[] Table(Encoding? enc)
    {
        var t = new char[256];
        for (int b = 0; b < 256; b++)
        {
            char c = enc is null ? (b is >= 0x20 and < 0x7F ? (char)b : '.') : enc.GetString([(byte)b])[0];
            t[b] = char.IsControl(c) || c == '�' ? '.' : c;
        }
        return t;
    }

    /// <summary>Reads one viewport and formats "offset  hex hex … hex  text" rows, an extra gap after 8 bytes.</summary>
    private static string[] Viewport(PagedDocument doc, long offset, byte[] buf, char[] line, int digits, char[] table)
    {
        int n = doc.Read(offset, buf);
        var rows = new string[(n + PerRow - 1) / PerRow];
        for (int r = 0; r < rows.Length; r++)
        {
            long off = offset + r * PerRow;
            int p = 0;
            for (int d = digits - 1; d >= 0; d--) line[p++] = "0123456789ABCDEF"[(int)(off >> (d * 4)) & 15];
            line[p++] = ' '; line[p++] = ' ';
            int count = Math.Min(PerRow, n - r * PerRow);
            for (int i = 0; i < PerRow; i++)
            {
                if (i == 8) line[p++] = ' ';
                if (i < count)
                {
                    byte b = buf[r * PerRow + i];
                    line[p++] = "0123456789ABCDEF"[b >> 4];
                    line[p++] = "0123456789ABCDEF"[b & 15];
                }
                else { line[p++] = ' '; line[p++] = ' '; }
                line[p++] = ' ';
            }
            line[p++] = ' ';
            for (int i = 0; i < count; i++) line[p++] = table[buf[r * PerRow + i]];
            rows[r] = new string(line, 0, p);
        }
        return rows;
    }

    /// <summary>Hex pattern with byte and nibble wildcards ("4E ?? 5?"). Matching uses the longest fixed run as a SIMD anchor.</summary>
    internal sealed class BytePattern
    {
        public required byte[] Value { get; init; }
        public required byte[] Mask { get; init; }
        public int AnchorStart { get; init; }
        public int AnchorLength { get; init; }
        public int Length => Value.Length;
        public bool HasWildcard => Mask.Any(m => m != 0xFF);

        public static BytePattern Parse(string hex)
        {
            var tokens = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var value = new byte[tokens.Length];
            var mask = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Length != 2) throw new FormatException($"Token '{tokens[i]}' is not two hex digits.");
                int hi = Nibble(tokens[i][0]), lo = Nibble(tokens[i][1]);
                mask[i] = (byte)((hi < 0 ? 0 : 0xF0) | (lo < 0 ? 0 : 0x0F));
                value[i] = (byte)(((hi < 0 ? 0 : hi) << 4) | (lo < 0 ? 0 : lo));
            }
            int bestStart = 0, bestLen = 0;
            for (int i = 0; i < mask.Length;)
            {
                int j = i;
                while (j < mask.Length && mask[j] == 0xFF) j++;
                if (j - i > bestLen) { bestStart = i; bestLen = j - i; }
                i = j + 1;
            }
            if (bestLen == 0) throw new FormatException("The pattern needs at least one fixed byte.");
            return new BytePattern { Value = value, Mask = mask, AnchorStart = bestStart, AnchorLength = bestLen };
        }

        private static int Nibble(char c) => c == '?' ? -1 : Convert.ToInt32(c.ToString(), 16);

        public bool MatchesAt(ReadOnlySpan<byte> s)
        {
            for (int k = 0; k < Value.Length; k++)
                if ((s[k] & Mask[k]) != Value[k]) return false;
            return true;
        }

        /// <summary>Counts matches starting before <paramref name="limit"/>; <paramref name="span"/> may extend past it by Length - 1 bytes. Overlapping matches count.</summary>
        public long Count(ReadOnlySpan<byte> span, int limit)
        {
            var anchor = Value.AsSpan(AnchorStart, AnchorLength);
            long n = 0;
            int pos = AnchorStart;
            while (pos < span.Length)
            {
                int i = span[pos..].IndexOf(anchor);
                if (i < 0) break;
                int a = pos + i, s = a - AnchorStart;
                if (s >= limit) break;
                if (s + Length <= span.Length && MatchesAt(span.Slice(s, Length))) n++;
                pos = a + 1;
            }
            return n;
        }
    }

    private static long Naive(ReadOnlySpan<byte> span, BytePattern p)
    {
        long n = 0;
        for (int s = 0; s + p.Length <= span.Length; s++)
            if (p.MatchesAt(span.Slice(s, p.Length))) n++;
        return n;
    }

    private const int Block = 16 << 20;

    /// <summary>Fixed 16 MiB blocks with a Length - 1 overlap, so neither line breaks nor block edges hide matches.</summary>
    private static Task<long> CountFile(MappedFile file, BytePattern p, long upTo) => Task.Run(() =>
    {
        int blocks = (int)((upTo + Block - 1) / Block);
        long total = 0;
        Parallel.For(0, blocks, () => 0L, (b, _, acc) =>
        {
            long start = (long)b * Block;
            int limit = (int)Math.Min(Block, upTo - start);
            var span = file.GetSpanClamped(start, Math.Min(limit + p.Length - 1, upTo - start));
            return acc + p.Count(span, limit);
        }, acc => Interlocked.Add(ref total, acc));
        return total;
    });

    /// <summary>The same over the piece tree: each worker copies its block out with <see cref="PagedDocument.Read"/> (concurrent readers, no writer).</summary>
    private static Task<long> CountDocument(PagedDocument doc, BytePattern p) => Task.Run(() =>
    {
        long len = doc.ByteLength;
        int blocks = (int)((len + Block - 1) / Block);
        long total = 0;
        Parallel.For(0, blocks, () => (Buf: new byte[Block + p.Length], N: 0L), (b, _, st) =>
        {
            long start = (long)b * Block;
            int limit = (int)Math.Min(Block, len - start);
            int got = doc.Read(start, st.Buf.AsSpan(0, (int)Math.Min(limit + p.Length - 1, len - start)));
            return (st.Buf, st.N + p.Count(st.Buf.AsSpan(0, got), limit));
        }, st => Interlocked.Add(ref total, st.N));
        return total;
    });

    private static long SequentialRead(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        var buf = new byte[8 << 20];
        long total = 0;
        int n;
        while ((n = fs.Read(buf)) > 0) total += n;
        return total;
    }

    private static void HashRange(PagedDocument doc, long from, long len, HashAlgorithmName alg)
    {
        using var h = IncrementalHash.CreateHash(alg);
        var buf = new byte[8 << 20];
        for (long done = 0; done < len;)
        {
            int n = doc.Read(from + done, buf.AsSpan(0, (int)Math.Min(buf.Length, len - done)));
            if (n == 0) throw new EndOfStreamException();
            h.AppendData(buf, 0, n);
            done += n;
        }
        h.GetHashAndReset();
    }

    private static double Entropy(ReadOnlySpan<byte> s)
    {
        Span<int> hist = stackalloc int[256];
        foreach (var b in s) hist[b]++;
        double e = 0;
        foreach (int c in hist)
            if (c > 0) { double q = (double)c / s.Length; e -= q * Math.Log2(q); }
        return e;
    }

    /// <summary>Minimap estimate: one sample of <paramref name="sample"/> bytes at the start of each bucket.</summary>
    private static double[] SampledEntropy(PagedDocument doc, int buckets, int sample)
    {
        var map = new double[buckets];
        var buf = new byte[sample];
        long step = doc.ByteLength / buckets;
        for (int i = 0; i < buckets; i++)
            map[i] = Entropy(buf.AsSpan(0, doc.Read(i * step, buf)));
        return map;
    }

    private static double[] FullEntropy(MappedFile file, int block)
    {
        var map = new double[(file.Length + block - 1) / block];
        Parallel.For(0, map.Length, i => map[i] = Entropy(file.GetSpanClamped((long)i * block, block)));
        return map;
    }

    /// <summary>Overwrite one byte and insert one byte in the middle, stream-save, verify against an independent hash; then patch a copy in place.</summary>
    private static async Task SaveScenario(string path, string name, long length, int reps, Action<string, List<double>, string> report)
    {
        Directory.CreateDirectory(Paths.Scratch);
        long m = length / 2, k = m + 4096;
        var parts = new (long Start, long Length, byte[]? Bytes)[] { (0, m, null), (0, 1, [0x41]), (m + 1, k - m - 1, null), (0, 1, [0x42]), (k, length - k, null) };
        string expected = HashPlan(path, parts);
        string outPath = Path.Combine(Paths.Scratch, "hex-save-" + name);
        var times = new List<double>();
        for (int r = 0; r < Math.Min(reps, 3); r++)
        {
            using var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false });
            doc.Replace(m, 1, [0x41]);
            doc.Insert(k, [0x42]);
            var sw = Stopwatch.StartNew();
            await doc.SaveAsync(outPath);
            double sec = sw.Elapsed.TotalSeconds;
            times.Add(sec);
            var hsw = Stopwatch.StartNew();
            string actual = Hashing.Sha256File(outPath);
            bool ok = actual == expected && new FileInfo(outPath).Length == length + 1;
            Console.WriteLine($"save rep {r}: {sec:F2}s, {length / sec / 1e9:F2} GB/s, verify {(ok ? "OK bytes identical" : "MISMATCH")} (sha256 {hsw.Elapsed.TotalSeconds:F1}s, {length / hsw.Elapsed.TotalSeconds / 1e9:F2} GB/s)");
            if (!ok) Environment.ExitCode = 1;
            File.Delete(outPath);
        }
        report("save-stream-overwrite+insert", times, "s");

        // overwrite-only edits keep the length, so a hex editor can patch the file in place instead of rewriting it
        string copy = Path.Combine(Paths.Scratch, "hex-patch-" + name);
        File.Copy(path, copy, true);
        var patch = new List<double>();
        for (int r = 0; r < 30; r++)
        {
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1))
            {
                fs.Position = m;
                fs.WriteByte(0x41);
                fs.Flush(true);
            }
            patch.Add(sw.Elapsed.TotalMilliseconds);
        }
        report("patch-in-place-1B-flush", patch, "ms");
        string expectedPatch = HashPlan(path, [(0, m, null), (0, 1, [0x41]), (m + 1, length - m - 1, null)]);
        bool pok = Hashing.Sha256File(copy) == expectedPatch;
        Console.WriteLine($"patch verify: {(pok ? "OK bytes identical" : "MISMATCH")}");
        if (!pok) Environment.ExitCode = 1;
        File.Delete(copy);
    }

    /// <summary>SHA-256 of the original file ranges and literal bytes in order, read independently of the piece tree.</summary>
    private static string HashPlan(string path, (long Start, long Length, byte[]? Bytes)[] parts)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fh = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var buf = new byte[8 << 20];
        foreach (var (start, len, bytes) in parts)
        {
            if (bytes is not null) { h.AppendData(bytes); continue; }
            for (long done = 0; done < len;)
            {
                int n = RandomAccess.Read(fh, buf.AsSpan(0, (int)Math.Min(buf.Length, len - done)), start + done);
                if (n <= 0) throw new EndOfStreamException();
                h.AppendData(buf, 0, n);
                done += n;
            }
        }
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }
}

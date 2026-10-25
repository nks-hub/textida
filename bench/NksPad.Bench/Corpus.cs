using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace NksPad.Bench;

/// <summary>Output encoding of a generated chunk; tracks byte budget and line count.</summary>
internal abstract class Sink(byte[] buf, int budget)
{
    public readonly byte[] Buf = buf;
    public readonly int Budget = budget;
    public int Pos;
    public long Lines;
    public abstract int UnitSize { get; }
    protected abstract int Cost(ReadOnlySpan<char> s);
    protected abstract void Write(ReadOnlySpan<char> s);
    public int Remaining => Budget - Pos;

    public bool Add(ReadOnlySpan<char> s)
    {
        int c = Cost(s);
        if (c > Remaining) return false;
        Write(s);
        Pos += c;
        Lines += s.Count('\n');
        return true;
    }
}

internal sealed class Utf8Sink(byte[] buf, int budget) : Sink(buf, budget)
{
    public override int UnitSize => 1;
    protected override int Cost(ReadOnlySpan<char> s) => Encoding.UTF8.GetByteCount(s);
    protected override void Write(ReadOnlySpan<char> s) => Encoding.UTF8.GetBytes(s, Buf.AsSpan(Pos));
}

internal sealed class Utf16Sink(byte[] buf, int budget) : Sink(buf, budget)
{
    public override int UnitSize => 2;
    protected override int Cost(ReadOnlySpan<char> s) => s.Length * 2;
    protected override void Write(ReadOnlySpan<char> s) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(s).CopyTo(Buf.AsSpan(Pos));
}

internal sealed class Cp1250Sink(byte[] buf, int budget) : Sink(buf, budget)
{
    private static readonly Encoding Enc = Encoding.GetEncoding(1250);
    public override int UnitSize => 1;
    protected override int Cost(ReadOnlySpan<char> s) => s.Length;
    protected override void Write(ReadOnlySpan<char> s) => Enc.GetBytes(s, Buf.AsSpan(Pos));
}

internal static class TextParts
{
    public static readonly string[] MsgAscii =
    [
        "connection established", "cache miss for key", "request completed", "retry scheduled after backoff",
        "session refreshed", "queue length within limits", "upstream returned timeout", "slow query detected",
        "token validated", "config reloaded", "handshake timeout while reading response", "payload accepted",
    ];

    public static readonly string[] MsgCz =
    [
        "požadavek byl úspěšně zpracován", "uživatel se přihlásil z nové adresy", "překročen časový limit spojení",
        "záloha dokončena bez chyb", "nepodařilo se načíst konfiguraci", "příliš dlouhá odpověď serveru, žádost zrušena",
        "ukládání dokumentu do úložiště selhalo", "připojení k databázi obnoveno", "žádný záznam nenalezen",
        "Příliš žluťoučký kůň úpěl ďábelské ódy",
    ];

    public static readonly string[] Words =
    [
        "alpha", "beta", "gamma", "delta", "kernel", "buffer", "stream", "editor", "řádek", "soubor", "hledání",
        "dokument", "výkon", "paměť", "zápis", "čtení", "index", "page", "token", "cache",
    ];
}

internal static class Corpus
{
    private const int ChunkSize = 16 << 20;
    private const int Reserve = 700;
    public const string RareLog = "NKSPAD_RARE_7741";
    public const string RareTree = "NKSPAD_TREE_RARE_5531";

    private static readonly string[] Levels = ["INFO ", "DEBUG", "WARN ", "ERROR"];
    private static readonly string[] Verbs = ["GET", "POST", "PUT", "GET", "GET", "DELETE"];
    private static readonly string[] UrlPaths = ["/api/v2/orders/", "/api/v2/users/", "/static/app.js?v=", "/health?n=", "/api/v1/items/"];

    private static int LogLine(Rng r, long chunk, long line, Span<char> b, bool injectRare)
    {
        int roll = r.Next(1000);
        int level = roll < 850 ? 0 : roll < 930 ? 1 : roll < 973 ? 2 : 3;
        int status = level == 3 ? 500 + r.Next(4) : r.Next(100) < 4 ? 404 : 200;
        int took = r.Next(100) < 3 ? 1000 + r.Next(30000) : 1 + r.Next(300);
        string msg = r.Next(100) < 35 ? TextParts.MsgCz[r.Next(TextParts.MsgCz.Length)] : TextParts.MsgAscii[r.Next(TextParts.MsgAscii.Length)];
        long sec = 1_000_000 + chunk * 120 + line / 400;
        var ts = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec);
        string rare = injectRare ? " " + RareLog : "";
        b.TryWrite(CultureInfo.InvariantCulture,
            $"{ts:yyyy-MM-dd HH:mm:ss}.{r.Next(1000):D3} {Levels[level]} [w-{r.Next(32)}] req={(uint)r.Next(int.MaxValue):x8} u{r.Next(90000)} {Verbs[r.Next(Verbs.Length)]} {UrlPaths[r.Next(UrlPaths.Length)]}{r.Next(1000000)} status={status} took={took}ms {msg}{rare}\n",
            out int n);
        return n;
    }

    private static void Pad(Sink s, string prefix, string suffix)
    {
        int chars = s.Remaining / s.UnitSize;
        if (chars == 0) return;
        if (chars < prefix.Length + suffix.Length + 1) throw new InvalidOperationException("pad too small: " + chars);
        s.Add(prefix + new string('x', chars - prefix.Length - suffix.Length) + suffix);
    }

    private static void FillLog(Sink s, Rng r, long chunk)
    {
        var b = new char[1024];
        long line = 0;
        while (s.Remaining > Reserve)
        {
            int n = LogLine(r, chunk, line, b, line == 1000 && chunk % 2 == 0);
            if (!s.Add(b.AsSpan(0, n))) break;
            line++;
        }
        Pad(s, "pad ", "\n");
    }

    private static void FillOneLine(Sink s, Rng r)
    {
        var sb = new StringBuilder();
        while (s.Remaining > Reserve)
        {
            sb.Clear();
            for (int i = 0; i < 64; i++) sb.Append(TextParts.Words[r.Next(TextParts.Words.Length)]).Append(' ');
            if (!s.Add(sb.ToString())) break;
        }
        Pad(s, "", "");
    }

    private static void FillJson(Sink s, Rng r, long chunk, bool first, bool last)
    {
        if (first) s.Add("[");
        long i = 0;
        while (s.Remaining > Reserve)
        {
            string name = TextParts.Words[r.Next(TextParts.Words.Length)];
            string msg = r.Next(100) < 35 ? TextParts.MsgCz[r.Next(TextParts.MsgCz.Length)] : TextParts.MsgAscii[r.Next(TextParts.MsgAscii.Length)];
            string obj = string.Create(CultureInfo.InvariantCulture,
                $"{{\"id\":{chunk * 100000 + i},\"ts\":{1_000_000 + chunk * 120 + i / 400},\"level\":\"{Levels[r.Next(4)].Trim()}\",\"user\":{{\"id\":{r.Next(90000)},\"name\":\"{name}\"}},\"tags\":[\"{name}\",\"t{r.Next(50)}\"],\"ok\":{(r.Next(2) == 0 ? "true" : "false")},\"took\":{r.Next(5000)}.{r.Next(100):D2},\"msg\":\"{msg}\"}},");
            if (!s.Add(obj)) break;
            i++;
        }
        Pad(s, "{\"pad\":\"", "\"},");
        if (last) s.Buf[s.Pos - 1] = (byte)']';
    }

    private static void FillCsv(Sink s, Rng r, long chunk, bool first)
    {
        if (first) s.Add("id,name,description,amount,created\r\n");
        long i = 0;
        var sb = new StringBuilder();
        while (s.Remaining > Reserve)
        {
            sb.Clear();
            sb.Append(chunk * 1_000_000 + i).Append(',').Append(TextParts.Words[r.Next(TextParts.Words.Length)]).Append(',');
            string msg = TextParts.MsgCz[r.Next(TextParts.MsgCz.Length)];
            int kind = r.Next(10);
            if (kind < 3) sb.Append('"').Append(msg).Append("\r\n(řádek 2 buňky)\n\"\"citát\"\", a čárka, v buňce\"");
            else if (kind < 6) sb.Append('"').Append(msg).Append(", \"\"uvozovky\"\"\"");
            else sb.Append(msg);
            sb.Append(',').Append(r.Next(100000)).Append('.').Append(r.Next(100).ToString("D2")).Append(",2026-10-").Append((1 + r.Next(28)).ToString("D2")).Append("\r\n");
            if (!s.Add(sb.ToString())) break;
            i++;
        }
        Pad(s, "0,pad,", ",0,2026-10-01\r\n");
    }

    private sealed record Spec(string Name, long Bytes, ulong Seed, Func<byte[], int, Sink> MakeSink, Action<Sink, Rng, long, long> Fill, byte[]? Prefix = null);

    private static Spec Log(string name, long bytes, ulong seed) =>
        new(name, bytes, seed, (b, n) => new Utf8Sink(b, n), (s, r, c, _) => FillLog(s, r, c));

    private static IEnumerable<Spec> Specs()
    {
        yield return Log("text-10MiB.log", 10L << 20, 1);
        yield return Log("text-100MiB.log", 100L << 20, 2);
        yield return Log("text-1GiB.log", 1L << 30, 3);
        yield return Log("text-10GiB.log", 10L << 30, 4);
        yield return Log("text-50GiB.log", 50L << 30, 5);
        yield return new("oneline-100MiB.txt", 100L << 20, 6, (b, n) => new Utf8Sink(b, n), (s, r, _, _) => FillOneLine(s, r));
        yield return new("json-100MiB.json", 100L << 20, 7, (b, n) => new Utf8Sink(b, n),
            (s, r, c, cnt) => FillJson(s, r, c, c == 0, c == cnt - 1));
        yield return new("csv-1GiB.csv", 1L << 30, 8, (b, n) => new Utf8Sink(b, n),
            (s, r, c, _) => FillCsv(s, r, c, c == 0));
        yield return new("utf16le-100MiB.txt", 100L << 20, 9, (b, n) => new Utf16Sink(b, n),
            (s, r, c, _) => FillLog(s, r, c), [0xFF, 0xFE]);
        yield return new("cp1250-10MiB.txt", 10L << 20, 10, (b, n) => new Cp1250Sink(b, n),
            (s, r, c, _) => FillLog(s, r, c));
    }

    private static bool Mac => Paths.Profile == "mac";
    private static int TreeMaxFileBytes => Mac ? 40 * 1024 : 200 * 1024;

    /// <summary>False for corpus items the active profile leaves out.</summary>
    public static bool InProfile(string name) => !(Mac && name == "text-50GiB.log");

    /// <summary>Arguments that are neither options nor option values.</summary>
    public static List<string> PositionalArgs(string[] args)
    {
        string[] withValue = ["--corpus", "--results", "--scratch", "--profile", "--rg"];
        var list = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (withValue.Contains(args[i])) { i++; continue; }
            if (!args[i].StartsWith("--")) list.Add(args[i]);
        }
        return list;
    }

    public static IReadOnlyList<string> Names => Specs().Select(s => s.Name).Append("tree").ToList();

    public static async Task GenerateAsync(string name, bool force)
    {
        Directory.CreateDirectory(Paths.Corpus);
        Directory.CreateDirectory(Paths.Manifest);
        if (name == "tree") { GenerateTree(force); return; }
        var spec = Specs().First(s => s.Name == name);
        string path = Path.Combine(Paths.Corpus, spec.Name);
        string side = Path.Combine(Paths.Manifest, spec.Name + ".txt");
        if (File.Exists(side) && !force) { Console.WriteLine($"skip {name}"); return; }
        var sw = Stopwatch.StartNew();
        int prefixLen = spec.Prefix?.Length ?? 0;
        long dataBytes = spec.Bytes - prefixLen;
        int chunks = (int)((dataBytes + ChunkSize - 1) / ChunkSize);
        int par = Math.Max(2, Environment.ProcessorCount - 4);
        var queue = Channel.CreateBounded<Task<(byte[] Buf, int Len, long Lines)>>(par * 2);
        var pool = new ConcurrentBag<byte[]>();

        var producer = Task.Run(async () =>
        {
            for (int c = 0; c < chunks; c++)
            {
                int ci = c;
                int size = (int)Math.Min(ChunkSize, dataBytes - (long)ci * ChunkSize);
                await queue.Writer.WriteAsync(Task.Run(() =>
                {
                    if (!pool.TryTake(out var buf)) buf = new byte[ChunkSize];
                    var sink = spec.MakeSink(buf, size);
                    var rng = new Rng(spec.Seed * 1_000_003UL + (ulong)ci);
                    spec.Fill(sink, rng, ci, chunks);
                    if (sink.Pos != size) throw new InvalidOperationException($"{name} chunk {ci}: {sink.Pos} != {size}");
                    return (buf, size, sink.Lines);
                }));
            }
            queue.Writer.Complete();
        });

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0, lines = 0;
        await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            Task pendingHash = Task.CompletedTask;
            byte[]? pendingBuf = null;
            if (spec.Prefix is { } p)
            {
                await fs.WriteAsync(p);
                hash.AppendData(p);
                total += p.Length;
            }
            int done = 0;
            await foreach (var t in queue.Reader.ReadAllAsync())
            {
                var (buf, len, ln) = await t;
                await pendingHash;
                if (pendingBuf is not null) pool.Add(pendingBuf);
                pendingBuf = buf;
                int l = len;
                pendingHash = Task.Run(() => hash.AppendData(buf, 0, l));
                await fs.WriteAsync(buf.AsMemory(0, len));
                total += len;
                lines += ln;
                if (++done % 64 == 0) Console.WriteLine($"{name}: {total / 1048576} MiB, {sw.Elapsed.TotalSeconds:F0}s");
            }
            await pendingHash;
        }
        await producer;
        if (total != spec.Bytes) throw new InvalidOperationException($"{name}: wrote {total}, wanted {spec.Bytes}");
        string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        File.WriteAllText(side, $"{spec.Name},{total},{sha},lines_lf={lines}\n");
        Console.WriteLine($"done {name}: {total} B, sha256={sha}, lf={lines}, {sw.Elapsed.TotalSeconds:F1}s");
    }

    private static string SourceLine(Rng r, int ext, bool rare)
    {
        string id = TextParts.Words[r.Next(TextParts.Words.Length)];
        int k = r.Next(12);
        string todo = r.Next(100) == 0 ? " // TODO: revisit" : "";
        string tail = rare ? " // " + RareTree : "";
        return ext switch
        {
            0 => k switch
            {
                0 => $"    public static int {id}{r.Next(1000)}(string arg) {{ return arg.Length + {r.Next(100)}; }}{todo}{tail}\n",
                1 => $"    private readonly Dictionary<string, int> _{id}{r.Next(100)} = new();{todo}{tail}\n",
                2 => $"        if ({id} == null) throw new ArgumentNullException(nameof({id}));{todo}{tail}\n",
                3 => $"namespace Demo.{id}{r.Next(50)};{tail}\n",
                _ => $"        var {id}{r.Next(100)} = await Load{id}Async({r.Next(1000)}, ct);{todo}{tail}\n",
            },
            1 => $"    function {id}{r.Next(1000)}($a, $b = {r.Next(100)}) {{ return $a . '{id}' . $b; }}{todo}{tail}\n",
            2 => $"  const {id}{r.Next(1000)} = (x, y) => x.{id}(y) + {r.Next(100)};{todo}{tail}\n",
            3 => $"  \"{id}{r.Next(1000)}\": {{ \"value\": {r.Next(100000)}, \"enabled\": {(r.Next(2) == 0 ? "true" : "false")} }},\n",
            _ => $"- {id} {r.Next(1000)}: popis řádku dokumentace{todo}{tail}\n",
        };
    }

    private static void GenerateTree(bool force)
    {
        string root = Path.Combine(Paths.Corpus, "tree");
        string side = Path.Combine(Paths.Manifest, "tree.txt");
        if (File.Exists(side) && !force) { Console.WriteLine("skip tree"); return; }
        const int files = 100_000, perDir = 100;
        string[] exts = [".cs", ".php", ".js", ".json", ".md"];
        for (int d = 0; d < files / perDir; d++) Directory.CreateDirectory(Path.Combine(root, $"d{d:D4}"));
        var hashes = new string[files];
        var sizes = new long[files];
        int done = 0;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, () => new byte[256 << 10], (i, _, buf) =>
        {
            var r = new Rng(0xC0FFEEUL + (ulong)i);
            int ext = i % exts.Length;
            int size = 1024 + r.Next(TreeMaxFileBytes - 1024 + 1);
            bool rare = i % 5000 == 17;
            var sink = new Utf8Sink(buf, size);
            int line = 0;
            while (sink.Remaining > Reserve)
            {
                if (!sink.Add(SourceLine(r, ext, rare && line == 3))) break;
                line++;
            }
            Pad(sink, "// pad ", "\n");
            string rel = Path.Combine($"d{i / perDir:D4}", $"f{i:D6}{exts[ext]}");
            File.WriteAllBytes(Path.Combine(root, rel), buf.AsSpan(0, size));
            hashes[i] = rel.Replace('\\', '/') + ":" + Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, size))).ToLowerInvariant();
            sizes[i] = size;
            int dn = Interlocked.Increment(ref done);
            if (dn % 10000 == 0) Console.WriteLine($"tree: {dn} files, {sw.Elapsed.TotalSeconds:F0}s");
            return buf;
        }, _ => { });
        string agg = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', hashes)))).ToLowerInvariant();
        File.WriteAllLines(Path.Combine(Paths.Manifest, "tree.files.txt"), hashes);
        File.WriteAllText(side, $"tree,{sizes.Sum()},{agg},files={files}\n");
        Console.WriteLine($"done tree: {files} files, {sizes.Sum()} B, aggregate sha256={agg}, {sw.Elapsed.TotalSeconds:F1}s");
    }

    public static void WriteManifest()
    {
        var lines = Directory.GetFiles(Paths.Manifest, "*.txt").Where(f => !f.EndsWith(".files.txt")).OrderBy(f => f).SelectMany(File.ReadAllLines);
        File.WriteAllLines(Path.Combine(Paths.Corpus, "MANIFEST.csv"), lines.Prepend("name,bytes,sha256,extra"));
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Bench;

/// <summary>
/// P2.7.1: cost of the stateful tokenizer. <c>syntax [--samples N] [--large path] [--large-gib N] [--skip-large] [--skip-long-line]</c>.
/// Keystroke cost = the document edit (which updates the line states) plus tokenizing the viewport again.
/// The budget is the typing p95 of 33 ms end to end (docs/p0/REPORT.md); what the window already needs without highlighting is taken from
/// the headless UI timing of a 1 MiB file in the full window (config F: p50 10.7 ms, p95 26.4 ms), so the highlighter has the difference.
/// </summary>
internal static class SyntaxBench
{
    private const double BudgetMs = 33.0;
    private const double RenderP50Ms = 10.7;
    private const double RenderP95Ms = 26.4;
    private const int ViewLines = 60;

    private static Csv? _csv;

    public static void Run(string[] args)
    {
        int samples = int.Parse(Args.Opt(args, "--samples") ?? "3000", CultureInfo.InvariantCulture);
        _csv = new Csv("syntax.csv", "scenario,grammar,metric,n,p50_ms,p95_ms,p99_ms,max_ms");
        Console.WriteLine($"Syntax tokenizer, {samples} keystrokes per scenario, viewport {ViewLines} lines, budget {BudgetMs} ms p95 end to end,");
        Console.WriteLine($"window without highlighting (1 MiB, full window): p50 {RenderP50Ms} ms, p95 {RenderP95Ms} ms, so the highlighter has {BudgetMs - RenderP95Ms:F1} ms at p95.");
        Warm();

        foreach (var g in new[] { "javascript", "csharp", "html" }) SmallFile(g, samples);
        if (!Args.Flag(args, "--skip-large")) foreach (var g in new[] { "plaintext", "javascript" }) LargeFile(args, g, samples);
        if (!Args.Flag(args, "--skip-long-line")) LongLine(args);
        _csv.Dispose();
    }

    private static void Warm()
    {
        var sw = Stopwatch.StartNew();
        BuiltinGrammars.Registry.Validate();
        Console.WriteLine($"all {BuiltinGrammars.Registry.All.Count()} grammars compiled, second validation {sw.Elapsed.TotalMilliseconds:F1} ms (regex compiled: {BuiltinGrammars.Registry.CompileRegexes})");
    }

    private static void Report(string scenario, string grammar, string metric, List<double> ms, string? note = null)
    {
        double p50 = Stats.Percentile(ms, .5), p95 = Stats.Percentile(ms, .95), p99 = Stats.Percentile(ms, .99), max = ms.Max();
        string verdict = note ?? "";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {metric,-34} n={ms.Count,-5} p50={p50,8:F3}  p95={p95,8:F3}  p99={p99,8:F3}  max={max,9:F3} ms {verdict}"));
        _csv!.Row(scenario, grammar, metric, ms.Count, p50.ToString("F4", CultureInfo.InvariantCulture), p95.ToString("F4", CultureInfo.InvariantCulture), p99.ToString("F4", CultureInfo.InvariantCulture), max.ToString("F4", CultureInfo.InvariantCulture));
    }

    private static string Verdict(List<double> total)
    {
        double p95 = Stats.Percentile(total, .95);
        double left = BudgetMs - RenderP95Ms;
        return p95 <= left ? $"-> within the {left:F1} ms left at p95" : $"-> OVER the {left:F1} ms left at p95";
    }

    // ---- 1 MiB file ----

    private static string CodeText(string grammar, int bytes, ulong seed)
    {
        var r = new Rng(seed);
        string[] cs =
        [
            "    public static int Compute{0}(string arg) {{ return arg.Length + {0}; }} // note {0}",
            "    private readonly Dictionary<string, int> _items{0} = new();",
            "        if (value{0} == null) throw new ArgumentNullException(nameof(value{0}));",
            "        var result{0} = await LoadAsync($\"item-{{{0}}}\", ct).ConfigureAwait(false);",
            "    /// <summary>Returns the number {0}.</summary>",
            "        string text{0} = @\"verbatim \"\"quoted\"\" {0}\";",
            "        for (int i = 0; i < {0}; i++) total += i * 2;",
            "",
        ];
        string[] js =
        [
            "  const handler{0} = (event, ctx) => ctx.items.map(x => x.id + {0}).filter(Boolean);",
            "  function compute{0}(a, b = {0}) {{ return `value ${{a}} and ${{b}}`; }} // note {0}",
            "  if (state.value{0} === null) {{ throw new Error('missing value {0}'); }}",
            "  const re{0} = /^[a-z]+{0}$/i.test(input) ? 'yes' : \"no\";",
            "  /** Returns the number {0}. */",
            "  export default {{ name: 'item{0}', size: {0}, tags: ['a', 'b'] }};",
            "",
        ];
        string[] html =
        [
            "<div class=\"card card-{0}\" id=\"c{0}\"><a href=\"/items/{0}\">Item {0}</a> &amp; <b>more</b></div>",
            "<?php echo htmlspecialchars($items[{0}]['name']); /* note {0} */ ?>",
            "<script>var x{0} = {{ id: {0}, name: \"n{0}\" }}; // note</script>",
            "<style>.card-{0} {{ color: #{0:x6}; margin: {0}px 0 1.5em; }}</style>",
            "<!-- section {0} -->",
            "<p>Plain text of the paragraph number {0}, with some words in it.</p>",
            "",
        ];
        var templates = grammar == "csharp" ? cs : grammar == "javascript" ? js : html;
        string open = grammar == "html" ? "<!--" : "/*", close = grammar == "html" ? "-->" : "*/";
        var sb = new StringBuilder(bytes + 256);
        int line = 0;
        while (sb.Length < bytes)
        {
            if (line % 150 == 149)
            {
                sb.Append("    ").Append(open).Append(" block comment ").Append(line).Append('\n');
                for (int i = 0; i < 4; i++) sb.Append("     text inside the comment ").Append(r.Next(1000)).Append('\n');
                sb.Append("    ").Append(close).Append('\n');
                line += 6;
                continue;
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, templates[r.Next(templates.Length)], r.Next(100000)).Append('\n');
            line++;
        }
        return sb.ToString();
    }

    private static void WaitForIndex(Document doc)
    {
        while (!doc.LineIndexState.Complete) Thread.Sleep(1);
    }

    private static void SmallFile(string grammar, int samples)
    {
        string text = CodeText(grammar, 1 << 20, 11);
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        var options = new DocumentOptions { Format = TextCodec.Utf8 };
        using var doc = Document.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance, options);
        WaitForIndex(doc);
        long lines = doc.LineIndexState.Lines;
        Console.WriteLine($"\n[1 MiB {grammar}] {text.Length:N0} chars, {lines:N0} lines");

        var first = new List<double>();
        foreach (var (name, top) in new[] { ("top", 0L), ("middle", lines / 2), ("end", lines - ViewLines) })
        {
            var cold = new List<double>();
            for (int i = 0; i < 25; i++)
            {
                var sw = Stopwatch.StartNew();
                using var s = new SyntaxSession(doc, tokenizer);
                s.TokenizeViewport(top, ViewLines);
                cold.Add(sw.Elapsed.TotalMilliseconds);
            }
            Report("1MiB-initial", grammar, $"initial viewport at {name}", cold);
            first.AddRange(cold);
        }

        using var session = new SyntaxSession(doc, tokenizer);
        long mid = lines / 2;
        session.TokenizeViewport(mid, ViewLines);
        var rng = new Rng(5);
        var edit = new List<double>();
        var tok = new List<double>();
        var total = new List<double>();
        var stateChanging = new List<double>();
        string[] typed = ["a", "e", " ", "(", "1", "x", ";", "."];
        for (int i = -300; i < samples; i++)
        {
            long line = mid + rng.Next(ViewLines);
            doc.TryGetOffset(new LinePosition(line, 0), out long start);
            long at = start + rng.Next(4);
            bool opens = i >= 0 && i % 50 == 0;
            byte[] insert = Encoding.UTF8.GetBytes(opens ? (grammar == "html" ? "<!--" : "/*") : typed[rng.Next(typed.Length)]);
            var sw = Stopwatch.StartNew();
            doc.Insert(at, insert);
            double e = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            session.TokenizeViewport(mid, ViewLines);
            double t = sw.Elapsed.TotalMilliseconds;
            if (i >= 0)
            {
                (opens ? stateChanging : total).Add(e + t);
                if (!opens)
                {
                    edit.Add(e);
                    tok.Add(t);
                }
            }
            doc.Undo();
            session.TokenizeViewport(mid, ViewLines);
        }

        Report("1MiB-keystroke", grammar, "edit incl. line state update", edit);
        Report("1MiB-keystroke", grammar, "retokenise viewport", tok);
        Report("1MiB-keystroke", grammar, "keystroke total", total, Verdict(total));
        Report("1MiB-keystroke", grammar, "keystroke opening a comment", stateChanging, Verdict(stateChanging));
    }

    // ---- 10 GiB log ----

    private static void MakeLog(string path, long bytes)
    {
        Console.WriteLine($"  creating {path} ({bytes >> 30} GiB), once");
        var r = new Rng(77);
        var block = new StringBuilder();
        string[] levels = ["INFO ", "DEBUG", "WARN ", "ERROR"];
        var ts = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        while (block.Length < (1 << 20) - 400)
        {
            ts = ts.AddMilliseconds(r.Next(50));
            block.Append(CultureInfo.InvariantCulture, $"{ts:yyyy-MM-dd HH:mm:ss}.{r.Next(1000):D3} {levels[r.Next(4)]} [w-{r.Next(32)}] req={(uint)r.Next(int.MaxValue):x8} GET /api/v2/orders/{r.Next(1000000)} status={200 + r.Next(3) * 100} took={r.Next(5000)}ms \"slow query detected\" // x\n");
        }
        var data = Encoding.UTF8.GetBytes(block.ToString());
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        for (long written = 0; written < bytes; written += data.Length) fs.Write(data, 0, (int)Math.Min(data.Length, bytes - written));
    }

    private static long LineStartNear(Document doc, long offset)
    {
        var buf = new byte[8192];
        long from = Math.Max(0, offset - buf.Length);
        int n = doc.Read(from, buf);
        int i = Array.LastIndexOf(buf, (byte)'\n', n - 1);
        return i < 0 ? from : from + i + 1;
    }

    private static void LargeFile(string[] args, string grammar, int samples)
    {
        string path = Args.Opt(args, "--large") ?? Path.Combine(Paths.Scratch, "syntax-log.log");
        long size = long.Parse(Args.Opt(args, "--large-gib") ?? "10", CultureInfo.InvariantCulture) << 30;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Console.WriteLine($"\n[{size >> 30} GiB log, {grammar}]");
        if (!File.Exists(path) || new FileInfo(path).Length < size) MakeLog(path, size);

        var sw = Stopwatch.StartNew();
        using var doc = Document.Open(path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        Console.WriteLine($"  Document.Open {sw.Elapsed.TotalSeconds:F1} s ({doc.ByteLength / 1073741824.0:F1} GiB, includes the fingerprint hash)");
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);

        var initial = new List<double>();
        foreach (var (name, at) in new[] { ("start", 0L), ("middle", doc.ByteLength / 2), ("near the end", doc.ByteLength - (1L << 20)) })
        {
            long top = at == 0 ? 0 : LineStartNear(doc, at);
            sw.Restart();
            var first = ViewportTokenizer.Tokenize(doc, tokenizer, top, ViewLines);
            double cold = sw.Elapsed.TotalMilliseconds;
            var again = new List<double>();
            for (int i = 0; i < 25; i++)
            {
                sw.Restart();
                ViewportTokenizer.Tokenize(doc, tokenizer, top, ViewLines);
                again.Add(sw.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  initial viewport at {name,-13}: first touch {cold:F3} ms ({first.Lines.Count} lines), repeated p50 {Stats.Percentile(again, .5):F3} p95 {Stats.Percentile(again, .95):F3} ms"));
            _csv!.Row("10GiB-initial", grammar, "initial viewport at " + name, 26, cold.ToString("F4", CultureInfo.InvariantCulture), Stats.Percentile(again, .95).ToString("F4", CultureInfo.InvariantCulture), "", "");
            initial.Add(cold);
        }

        long viewTop = LineStartNear(doc, doc.ByteLength / 2);
        var rng = new Rng(9);
        var tok = new List<double>();
        var edit = new List<double>();
        for (int i = -300; i < samples; i++)
        {
            long at = viewTop + rng.Next(2000);
            sw.Restart();
            doc.Insert(at, "a"u8.ToArray());
            double e = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            ViewportTokenizer.Tokenize(doc, tokenizer, viewTop, ViewLines);
            double t = sw.Elapsed.TotalMilliseconds;
            if (i >= 0)
            {
                edit.Add(e);
                tok.Add(t);
            }
            doc.Undo();
        }

        Report("10GiB-keystroke", grammar, "Document.Insert (not ours)", edit);
        Report("10GiB-keystroke", grammar, "retokenise viewport", tok, Verdict(tok));
    }

    // ---- one 100 MiB line ----

    private static void LongLine(string[] args)
    {
        string path = Path.Combine(Paths.Scratch, "syntax-oneline-100MiB.json");
        Directory.CreateDirectory(Paths.Scratch);
        const string unit = "{\"id\":12345,\"name\":\"item \\\"x\\\" é\",/* c */\"ok\":true,\"v\":[1.5e3,null,\"s\"]},";
        if (!File.Exists(path) || new FileInfo(path).Length < (100L << 20))
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var bytes = Encoding.UTF8.GetBytes(unit);
            for (long n = 0; n < 100L << 20; n += bytes.Length) fs.Write(bytes);
        }

        Console.WriteLine("\n[one line of 100 MiB, json, windows of 1 MiB]");
        using var doc = Document.Open(path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        var windows = new LongLineTokenizer(doc, BuiltinGrammars.Tokenizer("json"), 0);
        var perWindow = new List<double>();
        long tokens = 0;
        var total = Stopwatch.StartNew();
        for (int i = 0; ; i++)
        {
            var sw = Stopwatch.StartNew();
            var w = windows.GetWindow(i)!;
            perWindow.Add(sw.Elapsed.TotalMilliseconds);
            tokens += w.Tokens.Count;
            if (w.IsLast) break;
        }

        Report("100MiB-line", "json", "window of 1 MiB", perWindow);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  whole line {total.Elapsed.TotalSeconds:F2} s, {doc.ByteLength / 1048576.0 / total.Elapsed.TotalSeconds:F0} MiB/s, {tokens:N0} tokens"));
        var rewind = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            windows.GetWindow(70);
            rewind.Add(sw.Elapsed.TotalMilliseconds);
        }
        Report("100MiB-line", "json", "window 70 again (checkpoint)", rewind);
    }
}

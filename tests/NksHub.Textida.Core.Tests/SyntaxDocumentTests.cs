using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class SyntaxDocumentTests
{
    private static readonly string[] JsFragments =
    [
        "/*", "*/", "`", "${", "}", "'", "\"", "//", "x = 1;", "function f() {", "re = /a+/g", "\\", "é", "\n", "\n", "\r\n", "\r", " ", "foo(bar)",
    ];

    private static string[] SplitLines(string text) => Regex.Split(text, "\r\n|\n|\r");

    private static void WaitForIndex(Doc doc)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!doc.LineIndexState.Complete && DateTime.UtcNow < until) Thread.Sleep(1);
        Assert.True(doc.LineIndexState.Complete);
    }

    private static string Text(Doc doc, TestFormat format) => format.Text(doc.ReadBytes(new ByteRange(0, doc.ByteLength)));

    private static long ByteOffset(TestFormat format, string text, int chars) => format.Raw(text[..chars]).Length + format.Bom.Length;

    private static int Boundary(string model, int i)
    {
        while (i > 0 && i < model.Length && model[i - 1] == '\r' && model[i] == '\n') i++;
        return i;
    }

    [Theory]
    [InlineData("paged-utf8", 1)]
    [InlineData("reference-utf16", 2)]
    public void A_session_follows_random_document_edits_undo_and_redo(string kind, int seed)
    {
        var format = kind == "paged-utf8" ? TestFormat.Utf8 : TestFormat.Utf16LE;
        IBackendFactory factory = kind == "paged-utf8" ? PagedBackendFactory.Instance : ReferenceBackendFactory.Instance;
        var r = new Random(seed);
        var model = new StringBuilder();
        for (int i = 0; i < 250; i++) model.Append(JsFragments[r.Next(JsFragments.Length)]);

        using var doc = Doc.FromBytes(format.Bytes(model.ToString()), factory, new DocumentOptions { Format = format.Format });
        WaitForIndex(doc);
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        using var session = new SyntaxSession(doc, tokenizer);

        for (int step = 0; step < 300; step++)
        {
            string text = Text(doc, format);
            int op = r.Next(20);
            if (op < 10)
            {
                int at = Boundary(text, r.Next(text.Length + 1));
                string frag = string.Concat(Enumerable.Range(0, r.Next(1, 4)).Select(_ => JsFragments[r.Next(JsFragments.Length)]));
                Assert.True(doc.Insert(ByteOffset(format, text, at), format.Raw(frag)).Applied);
            }
            else if (op < 15 && text.Length > 2)
            {
                int a = Boundary(text, r.Next(text.Length)), b = Boundary(text, Math.Min(text.Length, a + r.Next(1, r.Next(3) == 0 ? 120 : 6)));
                if (b > a) Assert.True(doc.Delete(new ByteRange(ByteOffset(format, text, a), ByteOffset(format, text, b))).Applied);
            }
            else if (op < 17 && text.Length > 10)
            {
                int a = Boundary(text, r.Next(text.Length / 2)), b = Boundary(text, a + 1 + r.Next(5)), c = Boundary(text, text.Length / 2 + r.Next(text.Length / 2));
                if (b < c)
                    Assert.True(doc.Apply([new ByteEdit(new ByteRange(ByteOffset(format, text, a), ByteOffset(format, text, b)), format.Raw("/*")), ByteEdit.InsertAt(ByteOffset(format, text, c), format.Raw("*/\n"))]).Applied);
            }
            else if (op < 19) doc.Undo();
            else doc.Redo();

            WaitForIndex(doc);
            var lines = SplitLines(Text(doc, format));
            Assert.Equal(lines.Length, doc.LineIndexState.Lines);
            var full = SyntaxHelpers.TokenizeAll(tokenizer, lines);
            int first = r.Next(lines.Length), count = r.Next(1, 40);
            var actual = session.TokenizeViewport(first, count);
            Assert.Equal(Math.Min(count, lines.Length - first), actual.Count);
            for (int i = 0; i < actual.Count; i++)
            {
                Assert.True(ReferenceEquals(full[first + i].StartState, actual[i].StartState), $"step {step}: line {first + i} starts in '{actual[i].StartState}', expected '{full[first + i].StartState}'");
                Assert.True(full[first + i].Tokens.SequenceEqual(actual[i].Tokens), $"step {step}: tokens of line {first + i}");
            }
        }
    }

    [Fact]
    public void A_session_refuses_a_document_it_cannot_follow()
    {
        using var tmp = new TempFile(Encoding.UTF8.GetBytes("a\nb\n"));
        using var doc = Doc.Open(tmp.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.False(SyntaxSession.CanFollow(doc));
        Assert.Throws<NotSupportedException>(() => new SyntaxSession(doc, BuiltinGrammars.Tokenizer("json")));
    }

    private static (string Text, List<long> Starts, List<string> Lines) Corpus(int seed, int lines, bool longLine)
    {
        var r = new Random(seed);
        var sb = new StringBuilder();
        var starts = new List<long>();
        var all = new List<string>();
        string[] eols = ["\n", "\r\n", "\r", "\n"];
        long bytes = 0;
        for (int i = 0; i < lines; i++)
        {
            string line = i == 1500 && longLine
                ? "var big = " + string.Join(" + ", Enumerable.Range(0, 40_000).Select(n => n.ToString())) + ";"
                : string.Concat(Enumerable.Range(0, r.Next(0, 6)).Select(_ => JsFragments[r.Next(JsFragments.Length - 5)].Replace("\n", "")));
            string eol = eols[r.Next(eols.Length)];
            if (i > 0 && sb[^1] == (char)13 && line.Length == 0) line = "x";
            starts.Add(bytes);
            all.Add(line);
            sb.Append(line).Append(eol);
            bytes += Encoding.UTF8.GetByteCount(line) + eol.Length;
        }
        return (sb.ToString(), starts, all);
    }

    [Fact]
    public void A_viewport_from_a_byte_offset_equals_the_same_lines_of_a_full_pass()
    {
        var (text, starts, lines) = Corpus(7, 4000, longLine: true);
        using var tmp = new TempFile(Encoding.UTF8.GetBytes(text));
        using var doc = Doc.Open(tmp.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");
        var full = SyntaxHelpers.TokenizeAll(tokenizer, lines);

        foreach (int k in new[] { 0, 1, 777, 1499, 1500, 1501, 3950 })
        {
            var result = ViewportTokenizer.Tokenize(doc, tokenizer, starts[k], 40, full[k].StartState);
            int n = Math.Min(40, lines.Count - k);
            Assert.Equal(n, result.Lines.Count);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(starts[k + i], result.Lines[i].ByteStart);
                Assert.Equal(Encoding.UTF8.GetByteCount(lines[k + i]), result.Lines[i].ByteLength);
                Assert.False(result.Lines[i].Truncated);
                Assert.True(full[k + i].Tokens.SequenceEqual(result.Lines[i].Tokens), $"line {k + i}");
            }
            Assert.Equal(k + n < lines.Count ? starts[k + n] : doc.ByteLength, result.NextOffset);
            Assert.True(ReferenceEquals(full[k + n - 1].EndState, result.EndState));
        }
    }

    [Fact]
    public void A_line_over_the_read_limit_is_cut_and_the_next_line_is_still_found()
    {
        var (text, starts, lines) = Corpus(7, 3000, longLine: true);
        using var tmp = new TempFile(Encoding.UTF8.GetBytes(text));
        using var doc = Doc.Open(tmp.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var tokenizer = BuiltinGrammars.Tokenizer("javascript");

        var full = SyntaxHelpers.TokenizeAll(tokenizer, lines);
        var result = ViewportTokenizer.Tokenize(doc, tokenizer, starts[1499], 3, full[1499].StartState, maxLineBytes: 100_000);
        Assert.Equal([false, true, false], result.Lines.Select(l => l.Truncated).ToArray());
        Assert.Equal(starts[1500], result.Lines[1].ByteStart);
        Assert.True(result.Lines[1].ByteLength is > 90_000 and <= 100_000);
        Assert.Equal(starts[1501], result.Lines[2].ByteStart);
        Assert.Equal(starts[1502], result.NextOffset);
        Assert.True(full[1501].Tokens.SequenceEqual(result.Lines[2].Tokens));
    }

    [Fact]
    public void Viewport_tokenizing_refuses_wide_encodings()
    {
        using var doc = Doc.FromBytes(TestFormat.Utf16LE.Bytes("a\nb"), ReferenceBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf16LE.Format });
        Assert.Throws<NotSupportedException>(() => ViewportTokenizer.Tokenize(doc, BuiltinGrammars.Tokenizer("json"), 2, 5));
    }

    [Fact]
    public void Viewport_tokenizing_can_be_cancelled()
    {
        var (text, starts, _) = Corpus(3, 500, longLine: false);
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ViewportTokenizer.Tokenize(doc, BuiltinGrammars.Tokenizer("json"), 0, 50, ct: cts.Token));
    }

    private static string MakeLongLine(long bytes)
    {
        const string unit = "{\"id\":12345,\"name\":\"item \\\"x\\\" é\",/* c */\"ok\":true,\"v\":[1.5e3,null,\"s\"]},";
        var sb = new StringBuilder((int)Math.Min(bytes, int.MaxValue / 2));
        while (sb.Length < bytes - unit.Length) sb.Append(unit);
        return sb.ToString();
    }

    [Fact]
    public void A_long_line_in_windows_equals_the_line_tokenized_at_once()
    {
        string line = MakeLongLine(3_000_000) + "\nnext /* line */";
        using var tmp = new TempFile(Encoding.UTF8.GetBytes(line));
        using var doc = Doc.Open(tmp.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        string first = line[..line.IndexOf('\n')];
        var whole = new List<Token>();
        var endState = tokenizer.TokenizeLine(first, tokenizer.InitialState, whole);

        var windows = new LongLineTokenizer(doc, tokenizer, 0, windowBytes: 1 << 18);
        var joined = new List<Token>();
        LineWindow? w;
        int index = 0;
        long chars = 0;
        while ((w = windows.GetWindow(index++)) is not null)
        {
            Assert.Equal(chars, w.CharStart);
            chars += w.Chars;
            joined.AddRange(w.Tokens);
            if (w.IsLast) break;
        }

        Assert.True(windows.IsComplete);
        Assert.Equal(first.Length, chars);
        Assert.True(index > 8);
        Assert.True(SyntaxHelpers.Merge(whole).SequenceEqual(SyntaxHelpers.Merge(joined)), "tokens differ");
        Assert.True(ReferenceEquals(endState, windows.GetWindow(windows.KnownWindows - 1)!.EndState));
        Assert.Null(windows.GetWindow(windows.KnownWindows));
    }

    [Fact]
    public void A_window_far_into_a_long_line_is_found_again_from_its_checkpoint()
    {
        using var tmp = new TempFile(Encoding.UTF8.GetBytes(MakeLongLine(3_000_000)));
        using var doc = Doc.Open(tmp.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var windows = new LongLineTokenizer(doc, BuiltinGrammars.Tokenizer("json"), 0, windowBytes: 1 << 18);
        var far = windows.GetWindow(9)!;
        int known = windows.KnownWindows;
        var again = windows.GetWindow(9)!;
        Assert.Equal(known, windows.KnownWindows);
        Assert.True(far.Tokens.SequenceEqual(again.Tokens));
        Assert.Equal(far.CharStart, again.CharStart);
        Assert.True(windows.GetWindow(2)!.Tokens.Count > 1000);
    }

    [Fact]
    public void A_line_of_100_MiB_is_tokenized_in_windows_without_a_long_pause_and_can_be_cancelled()
    {
        using var tmp = new TempFile();
        string unit = MakeLongLine(1 << 20);
        using (var fs = new FileStream(tmp.Path, FileMode.Create, FileAccess.Write))
        {
            var bytes = Encoding.UTF8.GetBytes(unit);
            for (int i = 0; i < 100; i++) fs.Write(bytes);
        }

        using var doc = Doc.Open(tmp.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.True(doc.ByteLength > 100L * (1 << 20) - 100 * 200);
        var windows = new LongLineTokenizer(doc, BuiltinGrammars.Tokenizer("json"), 0);
        double slowest = 0;
        long tokens = 0, covered = 0;
        int index = 0;
        var total = Stopwatch.StartNew();
        while (true)
        {
            var sw = Stopwatch.StartNew();
            var w = windows.GetWindow(index++);
            slowest = Math.Max(slowest, sw.Elapsed.TotalMilliseconds);
            Assert.NotNull(w);
            tokens += w.Tokens.Count;
            covered += w.Chars;
            if (w.IsLast) break;
        }

        Assert.True(windows.IsComplete);
        Assert.True(windows.KnownWindows >= 100);
        Assert.True(tokens > 5_000_000);
        Assert.True(slowest < 2000, $"slowest window {slowest:F0} ms, total {total.Elapsed.TotalSeconds:F1} s");
        Assert.Equal(doc.ByteLength - 100 * (Encoding.UTF8.GetByteCount(unit) - unit.Length), covered);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fresh = new LongLineTokenizer(doc, BuiltinGrammars.Tokenizer("json"), 0);
        Assert.Throws<OperationCanceledException>(() => fresh.GetWindow(50, cts.Token));
        Assert.Equal(1, fresh.KnownWindows);
    }
}

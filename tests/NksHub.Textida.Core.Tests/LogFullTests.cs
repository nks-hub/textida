using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

public sealed class LogFullTests
{
    [Fact]
    public void Ansi_reads_colors_and_hides_every_escape_without_changing_the_text()
    {
        string line = "\x1b[1;31mERR\x1b[0m ok \x1b[38;5;82mG\x1b[48;2;1;2;3mB\x1b[4mU\x1b[m";
        var parsed = AnsiSgr.Parse(line);
        Assert.Equal(6, parsed.Escapes.Count);
        Assert.Equal(AnsiColor.Indexed(1), parsed.Spans[0].Style.Fore);
        Assert.True(parsed.Spans[0].Style.Bold);
        Assert.Equal(AnsiColor.Indexed(82), parsed.Spans[1].Style.Fore);
        Assert.Equal(AnsiColor.Rgb(1, 2, 3), parsed.Spans[2].Style.Back);
        Assert.True(parsed.Spans[3].Style.Underline);
        var rebuilt = new StringBuilder();
        int at = 0;
        foreach (var (start, length) in parsed.Escapes)
        {
            rebuilt.Append(line, at, start - at).Append(line, start, length);
            at = start + length;
        }
        Assert.Equal(line, rebuilt.Append(line, at, line.Length - at).ToString());
    }

    [Theory]
    [InlineData("a\x1b[31")]
    [InlineData("a\x1b")]
    [InlineData("a\x1b[3\x01m b")]
    [InlineData("a\x1b[38;5m b")]
    [InlineData("a\x1b[38;2;300;0;0m b")]
    public void Ansi_broken_sequences_stay_as_text_and_do_not_throw(string line)
    {
        var parsed = AnsiSgr.Parse(line);
        Assert.All(parsed.Spans, s => Assert.True(s.Start + s.Length <= line.Length));
        Assert.All(parsed.Escapes, e => Assert.True(e.Start >= 1 && e.Start + e.Length <= line.Length));
    }

    [Theory]
    [InlineData("   at App.Svc.Run() in C:\\src\\Svc.cs:line 42", StackTraceKind.DotNet, "C:\\src\\Svc.cs", 42)]
    [InlineData("\tat com.acme.Foo.bar(Foo.java:17)", StackTraceKind.Java, "Foo.java", 17)]
    [InlineData("  File \"/app/main.py\", line 9, in run", StackTraceKind.Python, "/app/main.py", 9)]
    [InlineData("#0 /var/www/a.php(12): run()", StackTraceKind.Php, "/var/www/a.php", 12)]
    [InlineData("    at run (/app/srv.js:5:7)", StackTraceKind.Node, "/app/srv.js", 5)]
    public void Stack_trace_frames_are_found_per_language(string line, StackTraceKind kind, string path, int number)
    {
        var link = Assert.Single(StackTraceLinks.Find(line));
        Assert.Equal((kind, path, number), (link.Kind, link.Path, link.Line));
        Assert.NotNull(StackTraceLinks.At(line, link.Start + 1));
    }

    [Fact]
    public void Stack_trace_relative_path_resolves_against_the_log_folder()
    {
        string dir = Directory.CreateTempSubdirectory("stk").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            File.WriteAllText(Path.Combine(dir, "src", "a.py"), "x");
            var link = StackTraceLinks.Find("  File \"src/a.py\", line 3, in f")[0];
            Assert.Equal(Path.Combine(dir, "src", "a.py"), StackTraceLinks.Resolve(link, [dir]));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Fold_ranges_join_runs_below_the_level_and_keep_continuations_with_their_entry()
    {
        string[] lines = ["INFO a", "INFO b", "ERROR c", "  at x", "DEBUG d", "INFO e", "WARN f"];
        var ranges = LogFold.Ranges(lines.Length, i => lines[i], LogLevelRules.Default, new LogFoldCriteria(LogLevel.Warning, null));
        Assert.Equal([new LogFoldRange(0, 1), new LogFoldRange(4, 5)], ranges);
        var byFilter = LogFold.Ranges(lines.Length, i => lines[i], LogLevelRules.Default, new LogFoldCriteria(null, new Regex("^(ERROR|WARN)")));
        Assert.Equal([new LogFoldRange(0, 1), new LogFoldRange(3, 5)], byFilter);
    }

    [Fact]
    public void Heatmap_puts_the_errors_of_a_generated_file_in_their_buckets_and_sampling_keeps_them()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 4000; i++) sb.Append(i >= 3000 ? "ERROR boom\n" : "INFO  fine\n"); // both 11 bytes, so the byte buckets split by line
        var snapshot = new Bytes(Encoding.UTF8.GetBytes(sb.ToString()));
        var full = LogHeat.Compute(snapshot, TextCodec.Utf8, 0, LogLevelRules.Default, 4, new LogHeatLimits(4096, 1 << 30, 1024));
        Assert.Equal(4000, full.Lines.Sum());
        Assert.Equal([0, 0, 0, 1000], full.Errors);
        var sampled = LogHeat.Compute(snapshot, TextCodec.Utf8, 0, LogLevelRules.Default, 4, new LogHeatLimits(4096, 100, 1024));
        Assert.True(sampled.Sampled);
        Assert.Equal(0, sampled.Errors[0]);
        Assert.Equal(1.0, sampled.ErrorDensity(3));
    }

    private sealed class Bytes(byte[] data) : IByteSnapshot
    {
        public long ByteLength => data.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(destination);
            return n;
        }
    }
}

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

    private static Func<long, string?> LinesOf(params string[] lines) => i => i >= 0 && i < lines.Length ? lines[i] : null;

    [Fact]
    public void A_style_a_line_leaves_open_goes_on_into_the_next_lines_until_a_code_changes_it()
    {
        var lines = LinesOf("\x1b[31mred start", "still red", "\x1b[1mbold and red", "plain text here \x1b[22m", "\x1b[0mreset", "after");
        var carry = new AnsiCarry(50);
        var red = new AnsiStyle(AnsiColor.Indexed(1), default, false, false);
        Assert.Equal(default, carry.StartOf(0, lines));
        Assert.Equal(red, carry.StartOf(1, lines));
        Assert.Equal(red, carry.StartOf(2, lines));
        Assert.Equal(red with { Bold = true }, carry.StartOf(3, lines));
        Assert.Equal(red, carry.StartOf(4, lines)); // bold off, the color stays
        Assert.Equal(default, carry.StartOf(5, lines)); // the reset of the line before
    }

    [Fact]
    public void A_line_without_escapes_in_a_styled_run_is_colored_whole_and_the_text_is_not_changed()
    {
        var lines = LinesOf("\x1b[32mgreen", "no escape at all", "ab\x1b[33mcd");
        var carry = new AnsiCarry(10);
        var plain = carry.Read(1, lines(1)!, lines);
        Assert.Empty(plain.Escapes);
        var span = Assert.Single(plain.Spans);
        Assert.Equal((0, 16, AnsiColor.Indexed(2)), (span.Start, span.Length, span.Style.Fore));

        var mixed = carry.Read(2, lines(2)!, lines); // the carried green up to the escape, then yellow
        Assert.Equal([(0, 2, AnsiColor.Indexed(2)), (7, 2, AnsiColor.Indexed(3))], mixed.Spans.Select(s => (s.Start, s.Length, s.Style.Fore)));
        Assert.Equal(AnsiColor.Indexed(3), mixed.End.Fore);
    }

    [Theory]
    [InlineData("\x1b[0m", true)]
    [InlineData("\x1b[m", true)]
    [InlineData("text \x1b[31mred\x1b[0m", true)]
    [InlineData("\x1b[0;31m", true)]
    [InlineData("\x1b[31;40;1;4m", true)] // every attribute set one by one
    [InlineData("\x1b[31m", false)]
    [InlineData("\x1b[31;1;4m", false)] // the background is not touched
    [InlineData("\x1b[38;2;1;2;3m\x1b[48;2;4;5;6m\x1b[1m\x1b[4m", true)] // even a color equal to the one the test uses
    [InlineData("plain", false)]
    [InlineData("", false)]
    public void A_line_whose_end_does_not_depend_on_its_start_is_told_from_one_that_does(string line, bool independent) =>
        Assert.Equal(independent, AnsiSgr.EndIsIndependent(line));

    [Fact]
    public void The_walk_back_stops_at_a_reset_and_at_the_limit_and_asks_for_no_line_it_does_not_need()
    {
        var asked = new List<long>();
        string?[] text = ["\x1b[0m\x1b[31mred", "x", "y", "z", "w"];
        Func<long, string?> lines = i => { asked.Add(i); return i >= 0 && i < text.Length ? text[i] : null; };
        var carry = new AnsiCarry(10);
        Assert.Equal(AnsiColor.Indexed(1), carry.StartOf(4, lines).Fore);
        Assert.Equal([3L, 2, 1, 0], asked); // line 0 resets everything, so nothing before it is asked for

        // a style that stays open for longer than the limit is not seen past it
        string[] open = ["\x1b[31mred", "a", "b", "c", "d"];
        Assert.Equal(AnsiColor.Indexed(1), new AnsiCarry(4).StartOf(4, LinesOf(open)).Fore);
        Assert.True(new AnsiCarry(3).StartOf(4, LinesOf(open)).IsDefault);
        Assert.True(new AnsiCarry(0).StartOf(4, LinesOf(open)).IsDefault);
    }

    [Fact]
    public void The_styles_lines_left_are_remembered_until_the_text_changes()
    {
        var carry = new AnsiCarry(10);
        var first = carry.Read(0, "\x1b[34mblue", LinesOf());
        Assert.Equal(AnsiColor.Indexed(4), first.End.Fore);
        // the next line is read from what the first one left, without asking for any line
        var second = carry.Read(1, "next", _ => throw new InvalidOperationException("no line is needed"));
        Assert.Equal(AnsiColor.Indexed(4), Assert.Single(second.Spans).Style.Fore);

        carry.Clear();
        Assert.True(carry.StartOf(1, LinesOf("changed", "x")).IsDefault);
    }

    [Fact]
    public void A_line_that_is_not_there_ends_the_walk_and_the_lines_before_it_count_as_default()
    {
        var carry = new AnsiCarry(10);
        Assert.True(carry.StartOf(3, i => i == 2 ? null : "\x1b[31mred").IsDefault);
        Assert.True(carry.StartOf(0, LinesOf("\x1b[31m")).IsDefault);
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

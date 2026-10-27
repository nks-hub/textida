using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.3.1: the LOG pattern language finds file, line and column.</summary>
public class LogPatternTests
{
    private static LogLocation? Mask(string mask, string line) => LogPattern.Compile(mask).Match(line);

    [Theory]
    [InlineData("*] %F(%L)", "[Error] uHLSett.pas(346): Undeclared identifier: 'ik'", "uHLSett.pas", 346, null)]
    [InlineData("* * %F %L:", "Error E2141 test.cpp 12: Declaration syntax error", "test.cpp", 12, null)]
    [InlineData("%F:%L:*", "main.c:12:5: error: expected ';'", "main.c", 12, null)]
    [InlineData("%F:%L:%C:*", "src/main.c:12:5: error: expected ';'", "src/main.c", 12, 5)]
    [InlineData("*on line %L", "PHP Parse error: syntax error in line on line 7", null, 7, null)]
    [InlineData("file:///%D%F:%L:", "file:///C:/docs/a.xml:3: parser error", "C:/docs/a.xml", 3, null)]
    [InlineData("%F(%L)%E", "Module.prg(41) Error: whatever (12) follows", "Module.prg", 41, null)]
    [InlineData("line %L column %C", "line 3 column 9 - Warning: x", null, 3, 9)]
    [InlineData("LINE %l COLUMN %c", "line 3 column 9 - Warning: x", null, 3, 9)]
    [InlineData("%F(%S%L,%S%C)", "Program.cs(  12,   5): error CS1002", "Program.cs", 12, 5)]
    [InlineData("%D%F:%L", "C:\\work\\a.c:8: x", "C:\\work\\a.c", 8, null)]
    [InlineData("%F:%L:*", "C:\\work\\a.c:8: x", null, 0, null)]
    public void Masks_of_compilers_read_file_line_and_column(string mask, string line, string? file, int number, int? column)
    {
        var hit = Mask(mask, line);
        if (number == 0) { Assert.Null(hit); return; }
        Assert.NotNull(hit);
        Assert.Equal(new LogLocation(file, number, column), hit);
    }

    [Theory]
    [InlineData("%F:%L:*", "no numbers here")]
    [InlineData("%F:%L:*", "main.c:x:1")]
    [InlineData("%F(%L)", "just text")]
    [InlineData("%F:%L", ":12")]
    [InlineData("%F:%L", "a.c:0")]
    [InlineData("%F:%L", "a.c:99999999999999999999")]
    [InlineData("*] %F(%L)", "no bracket here a.c(1)")]
    public void A_line_the_mask_does_not_fit_gives_no_place(string mask, string line) => Assert.Null(Mask(mask, line));

    [Theory]
    [InlineData("%F:%X", "Unknown code '%X'")]
    [InlineData("%F:", "needs %L")]
    [InlineData("100%", "Unknown code '%'")]
    public void An_invalid_mask_is_refused_with_the_reason(string mask, string expected) =>
        Assert.Contains(expected, Assert.Throws<LogPatternException>(() => LogPattern.Compile(mask)).Message);

    [Fact]
    public void A_regular_expression_names_its_parts_with_groups()
    {
        var pattern = LogPattern.Compile(@"^(?<file>[^:(]+)\((?<line>\d+),(?<column>\d+)\)", LogPatternSyntax.Regex);
        Assert.Equal(new LogLocation("Program.cs", 12, 5), pattern.Match("Program.cs(12,5): error CS1002"));
        Assert.Null(pattern.Match("nothing"));
        Assert.Contains("'line'", Assert.Throws<LogPatternException>(() => LogPattern.Compile(@"(?<file>\w+)", LogPatternSyntax.Regex)).Message);
        Assert.Contains("regular expression", Assert.Throws<LogPatternException>(() => LogPattern.Compile("(", LogPatternSyntax.Regex)).Message);
    }

    [Fact]
    public void The_preview_lists_every_line_of_a_sample_with_what_was_found()
    {
        var preview = LogPattern.Compile("%F:%L:%C:*").Preview("build started\r\na.c:3:7: error\nlink failed\n");
        Assert.Equal(4, preview.Count);
        Assert.Equal([null, new LogLocation("a.c", 3, 7), null, null], preview.Select(p => p.Location));
        Assert.Equal([1, 2, 3, 4], preview.Select(p => p.Number));
        Assert.Equal("a.c:3:7: error", preview[1].Text);
    }
}

/// <summary>P4.3.2: levels of log lines and the jump to the next or previous one without loading the file.</summary>
public class LogLevelTests
{
    [Theory]
    [InlineData("2026-01-01 ERROR something broke", LogLevel.Error)]
    [InlineData("[FATAL] out of memory", LogLevel.Error)]
    [InlineData("WARN: disk almost full", LogLevel.Warning)]
    [InlineData("INFO started", LogLevel.Info)]
    [InlineData("TRACE x=1", LogLevel.Debug)]
    [InlineData("a WARNING then an ERROR", LogLevel.Error)]
    public void Default_rules_name_the_most_severe_level_in_a_line(string line, LogLevel level) =>
        Assert.Equal(level, LogLevelRules.Default.Classify(line));

    [Theory]
    [InlineData("TERROR in the name")]
    [InlineData("an error in lower case")]
    [InlineData("")]
    public void Words_inside_other_words_and_other_cases_have_no_level(string line) => Assert.Null(LogLevelRules.Default.Classify(line));

    [Fact]
    public void Own_expressions_replace_the_defaults_and_a_level_without_one_is_never_assigned()
    {
        var rules = LogLevelRules.Create(new Dictionary<string, string> { ["error"] = @"\|E\|", ["warning"] = @"\|W\|" });
        Assert.Equal(LogLevel.Error, rules.Classify("12:00 |E| bad"));
        Assert.Equal(LogLevel.Warning, rules.Classify("12:00 |W| careful"));
        Assert.Null(rules.Classify("12:00 ERROR in the old style"));
        Assert.Null(rules.Classify("INFO"));
        Assert.Contains("Unknown level", Assert.Throws<LogPatternException>(() => LogLevelRules.Create(new Dictionary<string, string> { ["Error"] = "x" })).Message);
        Assert.Contains("regular expression", Assert.Throws<LogPatternException>(() => LogLevelRules.Create(new Dictionary<string, string> { ["error"] = "(" })).Message);
    }
}

public class LogScanTests
{
    private sealed class ArraySnapshot(byte[] bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
    }

    /// <summary>Lines of text built from the pieces below, written with the given codec and line ending; the offsets are noted while writing, not found by scanning.</summary>
    private static (byte[] Bytes, List<long> Starts, List<string> Lines) Build(Random random, TextCodec codec, int lines, string eolMode)
    {
        string[] samples = ["INFO request served in 12 ms", "DEBUG cache miss for key=ěščř", "WARN retry 2 of 5", "ERROR connection lost", "TRACE enter", "plain text without level", "WARNING then ERROR on one line", "TERROR is not a level", "", "error lower case"];
        var all = new List<byte>(codec.Bom);
        var starts = new List<long>();
        var texts = new List<string>();
        for (int i = 0; i < lines; i++)
        {
            string text = random.Next(40) == 0 ? "ERROR " + new string('x', random.Next(1, 3000)) : samples[random.Next(samples.Length)] + " #" + i;
            starts.Add(all.Count);
            texts.Add(text);
            string eol = eolMode switch { "lf" => "\n", "crlf" => "\r\n", "cr" => "\r", _ => random.Next(3) switch { 0 => "\n", 1 => "\r\n", _ => "\r" } };
            if (i == lines - 1 && random.Next(2) == 0) eol = "";
            all.AddRange(codec.Encode(text + eol));
        }
        return ([.. all], starts, texts);
    }

    [Theory]
    [InlineData("utf8", "lf")]
    [InlineData("utf8", "crlf")]
    [InlineData("utf8", "mixed")]
    [InlineData("utf8bom", "lf")]
    [InlineData("utf16", "crlf")]
    [InlineData("utf16be", "lf")]
    [InlineData("cp1250", "cr")]
    public void Next_and_previous_level_lines_agree_with_a_naive_walk_across_block_borders(string codecName, string eol)
    {
        var codec = codecName switch { "utf8bom" => TextCodec.Utf8Bom, "utf16" => TextCodec.Utf16LEBom, "utf16be" => TextCodec.Utf16BE, "cp1250" => TextCodec.Cp1250, _ => TextCodec.Utf8 };
        var random = new Random(codecName.Length * 31 + eol.Length);
        // a few MiB of text so that several blocks are read and the borders fall in the middle of lines
        var (bytes, starts, lines) = Build(random, codec, 50_000, eol);
        Assert.True(bytes.Length > 2 * LogScan.BlockBytes, bytes.Length.ToString());
        var snapshot = new ArraySnapshot(bytes);
        var rules = LogLevelRules.Default;
        long first = codec.Bom.Length;
        var expected = Enumerable.Range(0, lines.Count).Where(i => rules.Classify(lines[i]) == LogLevel.Error).Select(i => starts[i]).ToList();
        Assert.True(expected.Count > 100);

        var probes = new List<long> { first, bytes.Length };
        for (int i = 0; i < 30; i++) probes.Add(starts[random.Next(starts.Count)]);
        foreach (var e in expected.Where(_ => random.Next(150) == 0)) probes.Add(e);
        foreach (long from in probes)
        {
            Assert.Equal(expected.Cast<long?>().FirstOrDefault(o => o >= from), LogScan.Find(snapshot, codec, first, rules, LogLevel.Error, from, forward: true));
            Assert.Equal(expected.Cast<long?>().LastOrDefault(o => o < from), LogScan.Find(snapshot, codec, first, rules, LogLevel.Error, from, forward: false));
        }

        for (int i = 0; i < 40; i++)
        {
            int line = random.Next(starts.Count);
            // anywhere in the text of the line, not between the two bytes of a CRLF
            long inside = starts[line] + random.Next(codec.Encode(lines[line]).Length);
            inside -= inside % codec.Alignment;
            Assert.Equal(starts[line], LogScan.LineStart(snapshot, codec, first, inside));
            Assert.Equal(line + 1 < starts.Count ? starts[line + 1] : bytes.Length, LogScan.NextLineStart(snapshot, codec, starts[line]));
        }
    }

    [Fact]
    public void A_level_without_an_expression_finds_nothing_and_an_empty_log_is_fine()
    {
        var rules = LogLevelRules.Create(new Dictionary<string, string> { ["warning"] = "WARN" });
        var snapshot = new ArraySnapshot("ERROR a\nWARN b\n"u8.ToArray());
        Assert.Null(LogScan.Find(snapshot, TextCodec.Utf8, 0, rules, LogLevel.Error, 0, true));
        Assert.Equal(8, LogScan.Find(snapshot, TextCodec.Utf8, 0, rules, LogLevel.Warning, 0, true));
        Assert.Null(LogScan.Find(new ArraySnapshot([]), TextCodec.Utf8, 0, LogLevelRules.Default, LogLevel.Error, 0, true));
        Assert.Null(LogScan.Find(new ArraySnapshot([]), TextCodec.Utf8, 0, LogLevelRules.Default, LogLevel.Error, 0, false));
    }

    [Fact]
    public void The_search_can_be_cancelled_between_blocks()
    {
        var snapshot = new Generated(1 << 30);
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => LogScan.Find(snapshot, TextCodec.Utf8, 0, LogLevelRules.Default, LogLevel.Error, 0, true, scanned: _ => cts.Cancel(), ct: cts.Token));
        Assert.True(snapshot.BytesRead < 8 * LogScan.BlockBytes);
    }

    /// <summary>A log of fixed 64 byte lines made up on demand, so a size of 10 GiB costs no disk or memory. Line n is an ERROR line when <c>n</c> is in <see cref="ErrorLines"/>.</summary>
    private sealed class Generated(long length) : IByteSnapshot
    {
        public const int LineBytes = 64;
        public HashSet<long> ErrorLines { get; } = [];
        public long BytesRead;
        public long ByteLength => length;

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, length - offset);
            Interlocked.Add(ref BytesRead, count);
            Span<byte> line = stackalloc byte[LineBytes];
            for (int done = 0; done < count;)
            {
                long n = (offset + done) / LineBytes;
                int within = (int)((offset + done) % LineBytes);
                Fill(n, line);
                int take = Math.Min(LineBytes - within, count - done);
                line.Slice(within, take).CopyTo(destination[done..]);
                done += take;
            }
            return count;
        }

        private void Fill(long n, Span<byte> line)
        {
            string text = $"{n,12} {(ErrorLines.Contains(n) ? "ERROR" : "INFO ")} ";
            Encoding.ASCII.GetBytes(text, line);
            line[text.Length..].Fill((byte)'.');
            line[LineBytes - 1] = (byte)'\n';
        }
    }

    [Fact]
    public void A_jump_in_half_a_gibibyte_reads_little_more_than_the_distance_it_covers()
    {
        var log = new Generated(512L << 20);
        log.ErrorLines.UnionWith([1000, 5_000_000, 8_388_607]);
        var rules = LogLevelRules.Default;
        Assert.Equal(1000L * Generated.LineBytes, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 0, true));
        Assert.True(log.BytesRead <= 1000L * 64 + 2 * LogScan.BlockBytes, log.BytesRead.ToString());

        log.BytesRead = 0;
        long next = 1001L * Generated.LineBytes;
        Assert.Equal(5_000_000L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, next, true));
        Assert.True(log.BytesRead <= 5_000_000L * 64 - next + 2 * LogScan.BlockBytes, log.BytesRead.ToString());

        log.BytesRead = 0;
        Assert.Equal(8_388_607L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, log.ByteLength, false));
        Assert.True(log.BytesRead <= 64 + 2 * LogScan.BlockBytes, log.BytesRead.ToString());
        Assert.Equal(5_000_000L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 8_388_607L * 64, false));
        Assert.Null(LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 1000L * 64, false));
        Assert.Null(LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 8_388_608L * 64, true));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void In_ten_gibibytes_the_next_error_is_found_forward_and_backward()
    {
        var log = new Generated(10L << 30);
        long lines = log.ByteLength / Generated.LineBytes;
        log.ErrorLines.UnionWith([3, 100_000_000, lines - 2]);
        var rules = LogLevelRules.Default;
        Assert.Equal(3L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 0, true));
        Assert.Equal(100_000_000L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 4L * 64, true));
        Assert.Equal((lines - 2) * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 100_000_001L * 64, true));
        Assert.Equal(100_000_000L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, (lines - 2) * 64, false));
        Assert.Equal(3L * 64, LogScan.Find(log, TextCodec.Utf8, 0, rules, LogLevel.Error, 100_000_000L * 64, false));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void A_real_ten_gibibyte_file_is_searched_through_the_document_without_loading_it()
    {
        const long length = 10L << 30;
        using var temp = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new System.Diagnostics.ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", temp.Path }) command.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(command)!;
            process.WaitForExit();
        }
        byte[] head = "INFO start\nERROR first\nINFO again\n"u8.ToArray();
        byte[] tail = "\nWARN near the end\nERROR last\nINFO done\n"u8.ToArray();
        using (var stream = new FileStream(temp.Path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
            stream.Write(head);
            stream.Position = length - tail.Length;
            stream.Write(tail);
        }
        using var doc = Doc.Open(temp.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });
        var snapshot = doc.GetSnapshot();
        var rules = LogLevelRules.Default;
        long before = GC.GetTotalMemory(forceFullCollection: true);
        long firstError = LogScan.Find(snapshot, doc.Codec, doc.BomLength, rules, LogLevel.Error, 0, true) ?? -1;
        Assert.Equal(11, firstError);
        long lastError = length - tail.Length + "\nWARN near the end\n".Length;
        Assert.Equal(lastError, LogScan.Find(snapshot, doc.Codec, doc.BomLength, rules, LogLevel.Error, LogScan.NextLineStart(snapshot, doc.Codec, 11), true));
        Assert.Equal(11, LogScan.Find(snapshot, doc.Codec, doc.BomLength, rules, LogLevel.Error, lastError, false));
        Assert.True(GC.GetTotalMemory(forceFullCollection: true) - before < 256L << 20, "the search must not hold the file in memory");
    }
}

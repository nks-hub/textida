using System.Text.RegularExpressions;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class RegexToolsTests
{
    private static SearchPattern Rx(string text, bool caseSensitive = true) => new(text, IsRegex: true, CaseSensitive: caseSensitive);

    [Fact]
    public void Invalid_pattern_gives_a_reason_and_a_valid_one_none()
    {
        Assert.Null(RegexTools.Validate(Rx(@"(\d+)-(\w+)")));
        Assert.Null(RegexTools.Validate(Rx("")));
        Assert.NotNull(RegexTools.Validate(Rx("(abc")));
        var result = RegexTools.Test(Rx("[z-a]"), "x");
        Assert.NotNull(result.Error);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Test_lists_matches_and_groups_and_skips_empty_matches()
    {
        var r = RegexTools.Test(Rx(@"(?<key>\w+)=(\d+)"), "a=1 bb=22\nc=3");
        Assert.Null(r.Error);
        Assert.Equal(["a=1", "bb=22", "c=3"], r.Matches.Select(m => m.Value));
        var second = r.Matches[1];
        Assert.Equal(["bb=22", "22", "bb"], second.Groups.Select(g => g.Value));
        Assert.Empty(RegexTools.Test(Rx("x*"), "abc").Matches);
    }

    [Fact]
    public void Test_over_a_sample_finds_what_the_document_search_finds()
    {
        const string text = "foo bar\r\nfoo\nbar foo";
        string[] patterns = [@"foo", @"\bfoo\b", @"o+\r?$", @"^bar", @"(?<=foo )bar", @"foo\s+bar"];
        foreach (var p in patterns)
        {
            var pattern = Rx(p);
            var expected = RegexTools.Test(pattern, text).Matches.Select(m => (m.Index, m.Length)).ToList();
            var doc = Doc.FromBytes(System.Text.Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance);
            var (hits, _) = SearchWorker.CollectAsync(pattern, new SearchScope([], [LiveBuffer.Of(doc)])).GetAwaiter().GetResult();
            Assert.Equal(expected, hits.Select(h => ((int)h.Range.Start, (int)h.Range.Length)).ToList());
        }
    }

    [Fact]
    public void Multiline_match_crosses_line_ends()
    {
        var r = RegexTools.Test(Rx(@"a\r?\nb"), "xa\r\nb\na\nb");
        Assert.Equal(2, r.Matches.Count);
    }

    [Fact]
    public void A_pattern_over_the_time_limit_stops()
    {
        var r = RegexTools.Test(Rx(@"(?<=(a+)+)b"), new string('a', 40) + "c");
        Assert.True(r.TimedOut || r.Error is not null || r.Matches.Count == 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RegexTools.Test(Rx(@"(a+)+\1b"), new string('a', 60));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(@"(\w+) (\w+)", "$2 $1", "world hello")]
    [InlineData(@"(\w+) (\w+)", @"\U$1\E $2", "HELLO world")]
    [InlineData(@"(\w+) (\w+)", @"\u$1 \U$2", "Hello WORLD")]
    [InlineData(@"(\w+) (\w+)", @"\L$0", "hello world")]
    [InlineData(@"(\w+) (\w+)", @"a\\b$1", @"a\\bhello")]
    [InlineData(@"(\w+) (\w+)", @"\l$2-\u$1", "world-Hello")]
    public void Replacement_expands_groups_and_case_conversions(string pattern, string replacement, string expected)
    {
        var r = RegexTools.Test(Rx(pattern), "hello world", replacement);
        Assert.Equal(expected, r.Replaced);
    }

    [Fact]
    public void A_literal_search_takes_the_replacement_as_it_is()
    {
        var r = RegexTools.Test(new SearchPattern("a.b"), "a.b axb", @"\U$1");
        Assert.Equal(@"\U$1 axb", r.Replaced);
    }

    [Fact]
    public void Group_conversion_reaches_the_replace_plan()
    {
        var doc = Doc.FromBytes("name: ab"u8.ToArray(), ReferenceBackendFactory.Instance);
        var plan = ReplacePlanner.PlanAsync(Rx(@"(\w+)$"), @"\U$1", new SearchScope([], [LiveBuffer.Of(doc)])).GetAwaiter().GetResult();
        Assert.Equal("AB", plan.Proposal.Files.Single().Hunks.Single().NewText);
    }

    [Fact]
    public void Explain_describes_every_supported_construct()
    {
        var parts = RegexTools.Explain(@"^(?<y>\d{4})-(\d+?)\b[a-z\s]*(?:x|y)+(?=z)(?!q)(?<=a)(?<!b)(?>c)\1\k<y>\p{L}\x41\t\.$ (?i)(?#c)").ToList();
        string[] expected =
        [
            "startLine", "namedGroup", "digit", "quantRange", "groupEnd", "literal", "group", "digit", "quantPlus", "groupEnd", "wordBoundary", "charClass", "quantStar",
            "nonCapturing", "literal", "alternation", "literal", "groupEnd", "quantPlus", "lookahead", "literal", "groupEnd", "negativeLookahead", "literal", "groupEnd",
            "lookbehind", "literal", "groupEnd", "negativeLookbehind", "literal", "groupEnd", "atomic", "literal", "groupEnd", "backreference", "backreference", "property",
            "charCode", "controlChar", "literal", "endLine", "literal", "inlineOptions", "comment",
        ];
        Assert.Equal(expected, parts.Select(p => p.Kind));
        Assert.True(parts.First(p => p.Kind == "quantPlus" && p.Lazy is false).Token == "+");
        Assert.Contains(parts, p => p.Kind == "quantPlus" && p.Lazy);
        Assert.Equal("4", parts.First(p => p.Kind == "quantRange").Arg);
        Assert.Equal("a-z\\s", parts.First(p => p.Kind == "charClass").Arg);
        Assert.Equal(1, parts[2].Depth);
    }

    [Theory]
    [InlineData("a++", "possessive")]
    [InlineData(@"\Kx", "escape")]
    [InlineData("(?P<n>x)", "python")]
    [InlineData("(?R)", "recursion")]
    [InlineData("[[:alpha:]]", "posix")]
    [InlineData("(*SKIP)", "verb")]
    [InlineData("(?|a|b)", "branchReset")]
    public void Explain_names_constructs_of_other_dialects_as_unsupported(string pattern, string reason)
    {
        var part = RegexTools.Explain(pattern).First(p => p.Kind == "unsupported");
        Assert.Equal(reason, part.Arg);
    }
}

public class BracketMatcherTests
{
    private static IReadOnlyList<string> Lines(string text) => text.Replace("\r", "").Split('\n');

    [Fact]
    public void Plain_text_pairs_nested_brackets_across_lines_in_both_directions()
    {
        var lines = Lines("a(b[c]{\n  d\n}e)f");
        Assert.Equal(new TextPosition(2, 2), BracketMatcher.FindPair(lines, null, 0, 1));
        Assert.Equal(new TextPosition(0, 1), BracketMatcher.FindPair(lines, null, 2, 3));
        Assert.Equal(new TextPosition(0, 1), BracketMatcher.FindPair(Lines("x(y)"), null, 0, 4)); // the caret behind the last bracket
        Assert.Null(BracketMatcher.FindPair(lines, null, 0, 0));
        Assert.Null(BracketMatcher.FindPair(Lines("(("), null, 0, 0));
    }

    [Fact]
    public void Brackets_in_strings_and_comments_are_skipped_with_a_tokenizer()
    {
        var js = BuiltinGrammars.Tokenizer("javascript");
        var lines = Lines("f(\"a)\", /* ) */ b) // )\nx = ')'");
        Assert.Equal(new TextPosition(0, 17), BracketMatcher.FindPair(lines, js, 0, 1));
        Assert.Equal(new TextPosition(0, 1), BracketMatcher.FindPair(lines, js, 0, 17));
        Assert.Null(BracketMatcher.FindPair(lines, js, 1, 5)); // a bracket inside a string is not code
        // without the tokenizer the quoted bracket wins
        Assert.Equal(new TextPosition(0, 4), BracketMatcher.FindPair(lines, null, 0, 1));
    }

    [Fact]
    public void A_multi_line_comment_hides_brackets_on_later_lines()
    {
        var js = BuiltinGrammars.Tokenizer("javascript");
        var lines = Lines("a(\n/* \n) */\n)");
        Assert.Equal(new TextPosition(3, 0), BracketMatcher.FindPair(lines, js, 0, 1));
    }

    [Theory]
    [InlineData("12", GoToKind.Line, 12, 1)]
    [InlineData(" 12:5 ", GoToKind.Line, 12, 5)]
    [InlineData("3,7", GoToKind.Line, 3, 7)]
    [InlineData("40%", GoToKind.Percent, 40, 1)]
    [InlineData("@4096", GoToKind.Byte, 4096, 1)]
    public void Go_to_text_is_read(string text, GoToKind kind, long value, long column)
    {
        Assert.True(GoToTarget.TryParse(text, out var t));
        Assert.Equal(new GoToTarget(kind, value, column), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("101%")]
    [InlineData("3:0")]
    [InlineData("@")]
    public void Go_to_text_that_is_not_a_target_is_refused(string text) => Assert.False(GoToTarget.TryParse(text, out _));

}

public class RegexEngineTests
{
    private static SearchPattern Rx(string text) => new(text, IsRegex: true);

    private static bool IsLinear(SearchPattern p, RegexEngine engine = RegexEngine.Auto) => RegexTools.Create(p, engine).Options.HasFlag(RegexOptions.NonBacktracking);

    [Theory]
    [InlineData(@"foo\d+", true)]
    [InlineData(@"^\s+$", true)]
    [InlineData(@"\bword\b", true)]
    [InlineData(@"(a|b)+c", true)]
    [InlineData(@"(?<y>\d{4})-(\d\d)", true)]
    [InlineData(@"(a)\1", false)]
    [InlineData(@"(?<n>a)\k<n>", false)]
    [InlineData(@"(?<=x)y", false)]
    [InlineData(@"a(?!z)", false)]
    [InlineData(@"(?>a+)b", false)]
    public void Engine_follows_what_the_pattern_needs(string pattern, bool linear)
    {
        Assert.Equal(linear, IsLinear(Rx(pattern)));
        Assert.False(IsLinear(Rx(pattern), RegexEngine.Backtracking));
    }

    [Fact]
    public void Literal_and_whole_word_searches_use_the_linear_engine()
    {
        Assert.True(IsLinear(new SearchPattern("a.b(c)")));
        Assert.True(IsLinear(new SearchPattern("word", WholeWord: true, CaseSensitive: false)));
        Assert.False(IsLinear(new SearchPattern(@"(?<=a)b", IsRegex: true, WholeWord: true)));
    }

    [Fact]
    public void A_catastrophic_pattern_is_stopped_in_the_tester_and_in_the_search_worker()
    {
        var pattern = Rx(@"(a+)+\1b");
        string text = new string('a', 60) + "c";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(RegexTools.Test(pattern, text).TimedOut);

        var doc = Doc.FromBytes(System.Text.Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance);
        var (hits, summary) = SearchWorker.CollectAsync(pattern, new SearchScope([], [LiveBuffer.Of(doc)])).GetAwaiter().GetResult();
        Assert.Empty(hits);
        Assert.NotEmpty(summary.Errors);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
    }

    private static string ReplaceInDocument(string text, string pattern, string replacement)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var doc = Doc.FromBytes(bytes, ReferenceBackendFactory.Instance);
        var plan = ReplacePlanner.PlanAsync(Rx(pattern), replacement, new SearchScope([], [LiveBuffer.Of(doc)])).GetAwaiter().GetResult();
        var result = new List<byte>();
        long at = 0;
        foreach (var h in plan.Proposal.Files.Single().Hunks)
        {
            result.AddRange(bytes.AsSpan((int)at, (int)(h.Range.Start - at)).ToArray());
            result.AddRange(System.Text.Encoding.UTF8.GetBytes(h.NewText));
            at = h.Range.End;
        }
        result.AddRange(bytes.AsSpan((int)at).ToArray());
        return System.Text.Encoding.UTF8.GetString([.. result]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Anchors_without_text_insert_a_prefix_and_append_at_every_line(string eol)
    {
        string text = string.Join(eol, "a", "bc", "", "d");
        Assert.Equal(string.Join(eol, "> a", "> bc", "> ", "> d"), ReplaceInDocument(text, "^", "> "));
        Assert.Equal(string.Join(eol, "a;", "bc;", ";", "d;"), ReplaceInDocument(text, "$", ";"));
        Assert.Equal(string.Join(eol, "a;", "bc;", ";", "d;"), RegexTools.Test(Rx("$"), text, ";").Replaced);
        Assert.Equal(string.Join(eol, "> a", "> bc", "> ", "> d"), RegexTools.Test(Rx("^"), text, "> ").Replaced);
    }

    [Fact]
    public void Empty_matches_advance_like_the_standard_replace_loop()
    {
        Assert.Equal(Regex.Replace("abc", "x*", "-"), ReplaceInDocument("abc", "x*", "-"));
        Assert.Equal(Regex.Replace("abc", "x*", "-"), RegexTools.Test(Rx("x*"), "abc", "-").Replaced);
        Assert.Equal("[a]b[]c", RegexTools.Test(Rx("a|(?=c)"), "abc", "[$0]").Replaced);
    }

    public static TheoryData<string> LinearCorpus() =>
    [
        @"foo", @"\bfoo\b", @"\d+", @"[a-z]+\d*", @"^\w+", @"\w+$", @"(foo|bar)+", @"(\w+)@(\w+)\.com", @"a.*b", @"a.*?b", @"^$", @"\s+", @"x*",
        @"(?i)foo", @"[^\s]+", @"\p{L}+", @"(a|ab)(c|bcd)(d*)", @"colou?r", @"\d{2,4}", @"(?<y>\d{4})-(?<m>\d\d)", @"(\d+)(?:px|em)?", @"[aeiou]{2,}", @"^\s*$",
    ];

    [Theory]
    [MemberData(nameof(LinearCorpus))]
    public void Linear_and_backtracking_engines_find_the_same(string pattern)
    {
        string[] texts =
        [
            "foo bar foo\r\nbaz foo\nfoofoo bar 2024-05 12px 7em\n\n   \nab abcd abcd aab b\n",
            "mail me@example.com or you@site.com; colour color\r\n\r\nÁrvíztűrő tükörfúró 12345 aeiou\n",
            "",
        ];
        var linear = RegexTools.Create(Rx(pattern));
        var classic = RegexTools.Create(Rx(pattern), RegexEngine.Backtracking);
        Assert.True(linear.Options.HasFlag(RegexOptions.NonBacktracking));
        foreach (var text in texts)
        {
            static List<string> Describe(Regex r, string t) =>
                [.. r.Matches(t).Select(m => $"{m.Index}:{m.Length}:" + string.Join("|", m.Groups.Cast<Group>().Select(g => g.Success ? $"{g.Index},{g.Length}" : "-")))];
            Assert.Equal(Describe(classic, text), Describe(linear, text));
        }
    }

    private sealed class MemorySource(byte[] data) : IChunkSource
    {
        public long Length => data.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(destination);
            return n;
        }
    }

    [Fact]
    public void A_big_paged_file_is_searched_with_the_linear_engine()
    {
        var sb = new System.Text.StringBuilder();
        long expected = 0;
        for (int i = 0; sb.Length <= ContentScanner.LargeSource + (1 << 20); i++)
        {
            sb.Append("row ").Append(i).Append(" end\n");
            if (i % 10 == 7) expected++;
        }
        var source = new MemorySource(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
        Assert.True(source.Length > ContentScanner.LargeSource);

        var settings = new SearchSettings { Parallelism = 4 };
        var linear = CompiledPattern.Create(Rx(@"row \d*7 end"), settings.Engine);
        Assert.True(linear.Regex.Options.HasFlag(RegexOptions.NonBacktracking));
        var classic = CompiledPattern.Create(Rx(@"row \d*7 end"), RegexEngine.Backtracking);
        Assert.Equal(expected, ContentScanner.ScanLarge(source, linear, settings, null, 10, CancellationToken.None).Count);
        Assert.Equal(expected, ContentScanner.ScanLarge(source, classic, settings, null, 10, CancellationToken.None).Count);
    }
}

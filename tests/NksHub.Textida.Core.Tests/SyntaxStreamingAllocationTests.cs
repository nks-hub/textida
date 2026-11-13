using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class SyntaxStreamingAllocationTests(ITestOutputHelper output)
{
    private const string JsonPropertyPattern = "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"(?=[ \\t]*:)";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_256_kib_json_property_or_value_allocates_at_most_8_mib_after_warmup(bool property)
    {
        var tokenizer = BuiltinGrammars.Tokenizer("json");
        using (var warmDocument = Create("{\"warm\":\"a\\\"b\\\\c\"}"))
        using (var warmSession = new StreamingSyntaxSession(warmDocument, tokenizer))
            Complete(warmSession);
        string run = new('x', 256 << 10);
        string text = property ? "{\"" + run + "\":1}" : "{\"value\":\"" + run + "\"}";
        var expected = SyntaxHelpers.TokenizeAll(tokenizer, [text]).Single();
        using var document = Create(text);

        long before = GC.GetAllocatedBytesForCurrentThread();
        SyntaxWindow actual;
        using (var session = new StreamingSyntaxSession(document, tokenizer)) actual = Complete(session);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"property={property}; run-chars={run.Length}; allocated-bytes={allocated}; limit-bytes={8 << 20}");
        Assert.InRange(allocated, 0, 8L << 20);
        var line = Assert.Single(actual.Lines);
        var clipped = expected.Tokens.Where(token => token.Start < 64)
            .Select(token => token with { Length = Math.Min(token.End, 64) - token.Start }).ToArray();
        Assert.Equal(clipped, line.Tokens);
        Assert.Same(expected.StartState, line.StartState);
        Assert.Same(expected.EndState, line.EndState);
        Assert.Equal(document.Id, actual.DocumentId);
        Assert.Equal(document.Revision, actual.Revision);
    }

    [Fact]
    public void A_hundred_thousand_cached_or_known_eof_ensures_allocate_at_most_4_kib()
    {
        using var document = Create(new string('x', 32));
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        foreach (var _ in source.Ensure(0)) { }
        Assert.Equal(32L, source.Length);
        for (int i = 0; i < 256; i++) foreach (var _ in source.Ensure(CachedPosition(i))) { }
        long read = source.BytesRead;
        int progress = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
            foreach (var _ in source.Ensure(CachedPosition(i))) progress++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"ensure-calls=100000; allocated-bytes={allocated}; limit-bytes=4096");
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(0, progress);
        Assert.Equal(read, source.BytesRead);
        Assert.Equal('x', source.At(31));
        Assert.Equal(-1, source.At(32));
    }

    private static long CachedPosition(int i) => (i % 5) switch { 0 => -1, 1 => 32, _ => i & 31 };

    public static IEnumerable<object[]> MatchCases()
    {
        yield return ["greedy replay", "(?:[ab]|x[yz])*b", "aababb!", 0, false];
        yield return ["lazy", "(?:[ab]|x[yz])*?b", "aababb!", 0, false];
        yield return ["first branch priority", "(?:[ab]|ab){2}", "abx", 0, false];
        yield return ["escape fallback", "(?:[ab]|\\\\.)+c", "a\\xc!", 0, false];
        yield return ["lookahead", "(?:[ab]|\\\\.)*b(?=c)", "aababc", 0, false];
        yield return ["lookbehind", "(?<=(?:[ab]|\\\\.){2})c", "a\\xc!", 3, false];
        yield return ["negative lookbehind", "(?<!(?:[ab]|\\\\.){2})c", "z\\xc!", 3, false];
        const string reverse = "a\\xbc";
        yield return ["reverse", "(?:[ab]|\\\\.)*c", reverse, reverse.Length, true];
        string boundary = new string('a', SyntaxChunkReader.WindowBytes + 7) + "\\xbc";
        yield return ["escape across byte windows", "(?:[ab]|\\\\.)*c", boundary, 0, false];
        yield return ["reverse across byte windows", "(?:[ab]|\\\\.)*c", boundary, boundary.Length, true];
        yield return ["escaped property", JsonPropertyPattern, "\"a\\\"b\\\\c\" \t:0", 0, false];
        yield return ["property lookahead rejects value", JsonPropertyPattern, "\"a\\\"b\\\\c\" ,0", 0, false];
    }

    [Theory]
    [MemberData(nameof(MatchCases))]
    public void First_endpoint_matches_regex_with_cold_and_cached_windows(string name, string pattern, string text, int at, bool reverse)
    {
        var options = RegexOptions.CultureInvariant | (reverse ? RegexOptions.RightToLeft : RegexOptions.None);
        var reference = new Regex(pattern, options, TimeSpan.FromSeconds(5)).Match(text, at);
        long? expected = reference.Success && (reverse ? reference.Index + reference.Length == at : reference.Index == at)
            ? reverse ? reference.Index : reference.Index + reference.Length : null;
        var node = StreamPattern.Compile(pattern, true);
        using var document = Create(text);
        foreach (bool cached in new[] { false, true })
        {
            var source = new StreamingLineSource(document.GetSnapshot(), 0);
            if (cached) foreach (var _ in source.Ensure(reverse ? at - 1 : at)) { }
            var matcher = new StreamingMatcher(source);
            long? actual = null;
            foreach (var step in matcher.Match(node, at, reverse ? -1 : 1))
            {
                if (!step.IsMatch) continue;
                actual = step.End;
                break;
            }
            Assert.True(expected == actual, $"{name}; cached={cached}; expected-end={expected}; actual-end={actual}");
        }
    }

    [Fact]
    public void A_256_kib_repeated_choice_yields_at_least_256_progress_steps()
    {
        const int length = 256 << 10;
        using var document = Create(new string('x', length));
        var source = new StreamingLineSource(document.GetSnapshot(), 0);
        foreach (var _ in source.Ensure(0)) { }
        var matcher = new StreamingMatcher(source);
        var pattern = StreamPattern.Compile("(?:[x]|y)*", true);
        int progress = 0;
        long? end = null;
        foreach (var step in matcher.Match(pattern, 0))
        {
            if (!step.IsMatch) { progress++; continue; }
            end = step.End;
            break;
        }
        Assert.Equal((long)length, end);
        Assert.True(progress >= length / 1024, $"progress-steps={progress}; matched-units={length}");
    }

    private static Doc Create(string text) => Doc.FromBytes(Encoding.UTF8.GetBytes(text),
        new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });

    private static SyntaxWindow Complete(StreamingSyntaxSession session)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
        {
            var result = session.TokenizeWindow(0, 1, 0, 64, TimeSpan.FromMilliseconds(10));
            if (result.Status == SyntaxViewportStatus.Pending) continue;
            Assert.True(result.IsComplete);
            return result;
        }
        throw new TimeoutException("Streaming allocation fixture did not finish.");
    }
}

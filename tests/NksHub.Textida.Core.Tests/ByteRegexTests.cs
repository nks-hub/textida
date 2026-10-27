using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.6.1: regular expressions over bytes and the wildcard * with a settable N.</summary>
public class ByteRegexTests
{
    private static Doc Make(byte[] bytes) => Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });

    private static long[] Starts(Doc d, ByteQuery q, int chunk = 1 << 20)
    {
        var result = ByteSearch.Find(d.GetSnapshot(), q, new ByteSearchOptions { ChunkBytes = chunk, MaxResults = int.MaxValue });
        return [.. result.Matches.Select(m => m.Start)];
    }

    [Fact]
    public void A_class_pattern_finds_the_offsets_of_a_naive_pass_across_chunk_borders()
    {
        var random = new Random(11);
        var data = new byte[200_000];
        random.NextBytes(data);
        for (int i = 0; i < 300; i++) // plant matches and near misses, some on the borders of 4096-byte chunks
        {
            int at = i % 3 == 0 ? (random.Next(1, 40) * 4096) - random.Next(0, 3) : random.Next(0, data.Length - 3);
            data[at] = 0x50; data[at + 1] = 0x4B; data[at + 2] = (byte)(i % 2 == 0 ? random.Next(1, 9) : random.Next(9, 20));
        }
        var expected = new List<long>();
        for (int i = 0; i + 3 <= data.Length; i++)
            if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] is >= 1 and <= 8) expected.Add(i);
        Assert.True(expected.Count > 100);

        using var d = Make(data);
        var regex = ByteRegex.Parse(@"\x50\x4B[\x01-\x08]");
        Assert.Equal((3, 3), (regex.MinLength, regex.MaxLength));
        foreach (int chunk in new[] { 4096, 5000, 1 << 20 }) Assert.Equal(expected, Starts(d, regex, chunk));
    }

    [Fact]
    public void Anchors_match_only_at_the_start_and_end_of_the_range_in_every_chunk_size()
    {
        using var d = Make("abxabxabxab"u8.ToArray());
        foreach (int chunk in new[] { 1, 2, 3, 100 })
        {
            Assert.Equal([0L], Starts(d, ByteRegex.Parse("^ab"), chunk));
            Assert.Equal([9L], Starts(d, ByteRegex.Parse("ab$"), chunk));
            Assert.Equal([0L, 9L], Starts(d, ByteRegex.Parse("^ab|ab$"), chunk));
        }
        var inside = ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse("^ab"), new ByteSearchOptions { Range = new(3, 11), ChunkBytes = 2 });
        Assert.Equal([3L], inside.Matches.Select(m => m.Start));
    }

    [Fact]
    public void The_wildcard_star_reaches_at_most_n_bytes()
    {
        byte[] data = "A12B A1234B AB A123B".Select(c => (byte)c).ToArray();
        using var d = Make(data);

        var three = ByteRegex.ParseHexWildcard("41 * 42", maxRepeat: 3);
        Assert.Equal([0L, 12L, 15L], Starts(d, three)); // "A12B", "AB", "A123B"; "A1234B" has four bytes between
        var two = ByteRegex.ParseHexWildcard("41 * 42", maxRepeat: 2);
        Assert.Equal([0L, 12L], Starts(d, two));
        var four = ByteRegex.ParseHexWildcard("41 * 42", maxRepeat: 4);
        Assert.Equal([0L, 5L, 12L, 15L], Starts(d, four));

        Assert.Equal((2, 3 + 2), (three.MinLength, three.MaxLength));
        Assert.Equal([0L, 12L, 15L], Starts(d, ByteRegex.Parse("A.*B", maxRepeat: 3)));
    }

    [Fact]
    public void The_wildcard_has_to_know_n_and_a_range_of_your_own_needs_none()
    {
        Assert.Throws<FormatException>(() => ByteRegex.ParseHexWildcard("41 * 42"));
        Assert.Throws<FormatException>(() => ByteRegex.Parse("A.*B"));
        Assert.Throws<FormatException>(() => ByteRegex.Parse("A.{2,}B"));
        var own = ByteRegex.ParseHexWildcard("41 *{1,2} 42");
        Assert.Equal((3, 4), (own.MinLength, own.MaxLength));
        var counted = ByteRegex.Parse("a{2,5}b");
        Assert.Equal((3, 6), (counted.MinLength, counted.MaxLength));
    }

    [Fact]
    public void Half_bytes_and_any_byte_work_in_the_hex_form_like_the_plain_pattern()
    {
        byte[] data = [0x50, 0x4B, 0x03, 0x04, 0x50, 0x4B, 0x07, 0x14, 0x50, 0x5B, 0x03, 0x04];
        using var d = Make(data);
        var plain = BytePattern.ParseHex("50 4? ?? 04");
        var regex = ByteRegex.ParseHexWildcard("50 4? ?? 04", maxRepeat: 1);
        Assert.Equal(Starts(d, plain), Starts(d, regex));
        Assert.Equal([0L], Starts(d, regex));
    }

    [Fact]
    public void Matches_may_overlap_and_alternatives_take_the_first_that_fits()
    {
        using var d = Make("aaaa abcd ab"u8.ToArray());
        Assert.Equal([0L, 1L, 2L], Starts(d, ByteRegex.Parse("aa")));
        var alt = ByteRegex.Parse("ab|abcd");
        var found = ByteSearch.Find(d.GetSnapshot(), alt).Matches;
        Assert.Equal([(5L, 7L), (10L, 12L)], found.Select(m => (m.Start, m.End)));
        var longFirst = ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse("abcd|ab")).Matches;
        Assert.Equal([(5L, 9L), (10L, 12L)], longFirst.Select(m => (m.Start, m.End)));
    }

    [Fact]
    public void A_dot_is_any_byte_including_line_breaks_and_nul()
    {
        using var d = Make([0x61, 0x0A, 0x62, 0x61, 0x00, 0x62, 0x61, 0x0D, 0x62]);
        Assert.Equal([0L, 3L, 6L], Starts(d, ByteRegex.Parse("a.b")));
        Assert.Equal([0L, 6L], Starts(d, ByteRegex.Parse(@"a[\n\r]b")));
        Assert.Equal([3L], Starts(d, ByteRegex.Parse(@"a\0b")));
    }

    [Theory]
    [InlineData(@"\bword")]
    [InlineData("(a)\\1")]
    [InlineData("(?=a)b")]
    [InlineData("(?<n>a)")]
    [InlineData("a**")]
    [InlineData("[a-")]
    [InlineData("(ab")]
    [InlineData("ab)")]
    [InlineData(@"\x4")]
    [InlineData("a|")]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("a{3,1}")]
    [InlineData("a{")]
    public void Syntax_the_byte_dialect_does_not_have_is_refused(string pattern) =>
        Assert.Throws<FormatException>(() => ByteRegex.Parse(pattern, maxRepeat: 4));

    [Fact]
    public void A_pattern_that_can_match_nothing_is_refused()
    {
        Assert.Throws<FormatException>(() => ByteRegex.Parse("a*", maxRepeat: 3));
        Assert.Throws<FormatException>(() => ByteRegex.Parse("(ab)?", maxRepeat: 3));
        Assert.Throws<FormatException>(() => ByteRegex.Parse("a{0}"));
    }

    [Fact]
    public void Lengths_follow_the_syntax_tree()
    {
        var r = ByteRegex.Parse("a{2,5}b|cd(e|fgh)", maxRepeat: 9);
        Assert.Equal((3, 6), (r.MinLength, r.MaxLength));
        Assert.True(r.IsMatch("aab"u8));
        Assert.True(r.IsMatch("cdfgh"u8));
        Assert.False(r.IsMatch("ab"u8));
    }

    [Fact]
    public void A_search_in_an_edited_document_sees_the_edit_and_a_limit_is_reported()
    {
        using var d = Make("xxPK\u0001xx"u8.ToArray());
        Assert.True(d.Replace(new(0, 1), "P"u8.ToArray()).Applied);
        Assert.True(d.Insert(0, "PK\u0003"u8.ToArray()).Applied);
        var all = ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse(@"PK[\x01-\x08]"));
        Assert.Equal([0L, 5L], all.Matches.Select(m => m.Start));
        var limited = ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse(@"PK[\x01-\x08]"), new ByteSearchOptions { MaxResults = 1 });
        Assert.True(limited.LimitReached);
        Assert.Single(limited.Matches);
    }

    [Fact]
    public void A_range_keeps_matches_inside_it_and_the_cancellation_is_honoured()
    {
        using var d = Make("abcabcabc"u8.ToArray());
        var r = ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse("abc"), new ByteSearchOptions { Range = new(2, 8) });
        Assert.Equal([3L], r.Matches.Select(m => m.Start));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ByteSearch.Find(d.GetSnapshot(), ByteRegex.Parse("abc"), null, cts.Token));
    }
}

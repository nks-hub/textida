using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class BytePatternTests
{
    [Theory]
    [InlineData("0x1F40 i32", "401F0000")]
    [InlineData("0x1F40 i32 be", "00001F40")]
    [InlineData("-128 i8", "80")]
    [InlineData("255 u8", "FF")]
    [InlineData("-32768 i16", "0080")]
    [InlineData("65535 u16 be", "FFFF")]
    [InlineData("-2147483648 i32 be", "80000000")]
    [InlineData("4294967295 u32", "FFFFFFFF")]
    [InlineData("-9223372036854775808 i64", "0000000000000080")]
    [InlineData("18446744073709551615 u64 be", "FFFFFFFFFFFFFFFF")]
    [InlineData("-0x80 i8", "80")]
    [InlineData("1.5 f16", "003E")]
    [InlineData("-2.5 f32 be", "C0200000")]
    [InlineData("3.14 f64 be", "40091EB851EB851F")]
    [InlineData("-0 f64", "0000000000000080")]
    public void Typed_numbers_use_declared_width_and_endianness(string query, string bytes)
    {
        byte[] encoded = Convert.FromHexString(bytes);
        using var doc = Doc.FromBytes(encoded, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var pattern = BytePattern.ParseNumber(query);
        Assert.Equal(encoded.Length, pattern.Length);
        Assert.Equal(new ByteRange(0, encoded.Length), Assert.Single(ByteSearch.Find(doc.GetSnapshot(), pattern).Matches));
    }

    [Theory]
    [InlineData("128 i8")]
    [InlineData("-1 u8")]
    [InlineData("65536 u16")]
    [InlineData("0xFFFFFFFFFFFFFFFF i64")]
    [InlineData("18446744073709551616 u64")]
    [InlineData("1e10 f16")]
    [InlineData("1e100 f32")]
    [InlineData("1e400 f64")]
    public void Out_of_range_numbers_are_rejected(string query) => Assert.Throws<OverflowException>(() => BytePattern.ParseNumber(query));

    [Theory]
    [InlineData("1")]
    [InlineData("1 i128")]
    [InlineData("1 i32 sideways")]
    [InlineData("0x i8")]
    [InlineData("1.5 i8")]
    [InlineData("3,14 f32")]
    public void Invalid_number_syntax_is_rejected(string query) => Assert.Throws<FormatException>(() => BytePattern.ParseNumber(query));

    [Theory]
    [InlineData("0")]
    [InlineData("0 0")]
    [InlineData("GG")]
    [InlineData("0x01")]
    [InlineData("01 * 02")]
    public void Invalid_hex_syntax_is_rejected(string query) => Assert.Throws<FormatException>(() => BytePattern.ParseHex(query));

    [Fact]
    public void Empty_patterns_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => BytePattern.ParseHex(" \t"));
        Assert.Throws<ArgumentException>(() => BytePattern.FromText("", TextCodec.Utf8));
        Assert.Throws<ArgumentException>(() => BytePattern.FromBytes([]));
    }

    [Fact]
    public void Text_encoding_matches_content_without_searching_for_a_bom()
    {
        const string text = "Žluťoučký";
        foreach (var codec in new[] { TextCodec.Utf8Bom, TextCodec.Cp1250, TextCodec.Utf16LEBom, TextCodec.Utf16BEBom })
        {
            byte[] bytes = [.. codec.Bom, .. codec.Encode(text), .. codec.Encode("\n"), .. codec.Encode(text)];
            using var doc = Doc.FromBytes(bytes, ReferenceBackendFactory.Instance, new() { Format = codec });
            var result = ByteSearch.Find(doc.GetSnapshot(), BytePattern.FromText(text, codec), new() { ChunkBytes = 3 });
            Assert.Equal(new long[] { codec.Bom.Length, codec.Bom.Length + codec.Encode(text + "\n").Length }, result.Matches.Select(m => m.Start));
        }
        Assert.Throws<UnrepresentableCharactersException>(() => BytePattern.FromText("漢", TextCodec.Cp1250));
    }

    [Fact]
    public void A_pattern_keeps_a_copy_of_the_input_bytes()
    {
        byte[] bytes = [0x12, 0x34];
        var pattern = BytePattern.FromBytes(bytes);
        bytes[0] = 0;
        Assert.True(pattern.Matches([0x12, 0x34]));
        Assert.False(pattern.Matches(bytes));
    }
}

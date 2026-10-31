using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.5.3: the multi-byte Asian encodings. A character is one to four bytes; blocks are cut between characters, an invalid sequence keeps its bytes, and a character the encoding lacks is reported, never replaced.</summary>
public class MultiBytePagesTests
{
    private const string Japanese = "日本語のテキスト：こんにちは世界。半角ｶﾀｶﾅも使えます。 \\ ~ ABC 123\r\n二行目です。\n";
    private const string Simplified = "中文文本编码测试：你好，世界！这是第二行。\r\nABC 123 \\ ~\n";
    private const string Traditional = "中文文字編碼測試：你好，世界！這是第二行。\r\nABC 123 \\ ~\n";
    private const string Korean = "한국어 텍스트 인코딩 테스트: 안녕하세요, 세계!\r\nABC 123 \\ ~\n두 번째 줄입니다.\n";

    public static TheoryData<string, string> Samples => new()
    {
        { "Shift-JIS", Japanese }, { "EUC-JP", Japanese }, { "GBK", Simplified }, { "GB18030", Simplified + "𠀀 😀 ř\n" }, { "Big5", Traditional }, { "EUC-KR", Korean },
    };

    [Fact]
    public void The_six_encodings_are_listed_under_the_Asian_group_as_multi_byte()
    {
        foreach (var name in new[] { "Shift-JIS", "EUC-JP", "GBK", "GB18030", "Big5", "EUC-KR" })
        {
            var codec = TextCodec.ByName(name)!;
            Assert.Equal((CodecKind.MultiByte, EncodingGroup.Asian), (codec.Kind, codec.Group));
            Assert.Equal(1, codec.Alignment);
        }
        Assert.False(TextCodec.ByName("GB18030")!.IsLossy);
        Assert.True(TextCodec.ByName("Shift-JIS")!.IsLossy);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_text_goes_to_bytes_and_back_and_the_bytes_come_back_the_same(string name, string text)
    {
        var codec = TextCodec.ByName(name)!;
        var bytes = codec.Encode(text);
        var decoded = codec.Decode(bytes);
        Assert.Equal(text, decoded.Text);
        Assert.Equal(0, decoded.InvalidCount);
        Assert.Equal(bytes, codec.Encode(decoded.Text));
        for (int i = 0; i < decoded.Text.Length; i++)
            if (decoded.IsCharBoundary(i)) Assert.True(codec.IsBoundary(bytes, decoded.ByteOffsetOf(i)), $"{name}: character {i} starts at byte {decoded.ByteOffsetOf(i)}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Blocks_cut_in_the_middle_of_a_character_decode_to_the_same_text_and_map(string name, string text)
    {
        var codec = TextCodec.ByName(name)!;
        var bytes = codec.Encode(text);
        var whole = codec.Decode(bytes);
        foreach (int block in new[] { 4, 5, 6, 7, 9, 13, 64 })
        {
            var blocks = codec.DecodeInBlocks(bytes, block);
            Assert.Equal(whole.Text, blocks.Text);
            Assert.Equal(whole.InvalidCount, blocks.InvalidCount);
            for (int i = 0; i <= whole.Text.Length; i++) Assert.Equal(whole.ByteOffsetOf(i), blocks.ByteOffsetOf(i));
        }
        for (int k = 0; k <= bytes.Length; k++)
        {
            int complete = codec.CompleteLength(bytes.AsSpan(0, k));
            Assert.InRange(complete, 0, k);
            Assert.DoesNotContain('�', codec.GetString(bytes.AsSpan(0, complete))); // a prefix ends between two characters, never inside one
        }
        Assert.Equal(bytes.Length, codec.CompleteLength(bytes));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void The_units_read_one_by_one_add_up_to_the_whole_text(string name, string text)
    {
        var codec = TextCodec.ByName(name)!;
        var bytes = codec.Encode(text);
        int at = 0, chars = 0;
        while (at < bytes.Length)
        {
            at += codec.ReadUnit(bytes.AsSpan(at), out int n);
            chars += n;
        }
        Assert.Equal((bytes.Length, text.Length), (at, chars));
    }

    [Theory]
    [InlineData("Shift-JIS")]
    [InlineData("GBK")]
    [InlineData("Big5")]
    [InlineData("EUC-JP")]
    [InlineData("EUC-KR")]
    [InlineData("GB18030")]
    public void An_invalid_sequence_is_one_replacement_character_whose_bytes_stay_in_the_buffer(string name)
    {
        var codec = TextCodec.ByName(name)!;
        byte lead = name is "EUC-JP" or "EUC-KR" ? (byte)0xA1 : (byte)0x81; // a lead byte followed by a space, which is no trail byte
        byte[] bytes = [0x41, lead, 0x20, 0x42, 0x43];
        var decoded = codec.Decode(bytes);
        Assert.Equal(1, decoded.InvalidCount);
        Assert.Contains('�', decoded.Text);
        Assert.Equal(bytes, decoded.Bytes.ToArray()); // nothing is rewritten: only an edited range is ever encoded again
        Assert.StartsWith("A", decoded.Text);
        Assert.Contains(" ", decoded.Text);
        Assert.Equal(decoded.Text, codec.DecodeInBlocks(bytes, 4).Text);
    }

    [Fact]
    public void A_character_the_encoding_lacks_is_reported_and_not_replaced_and_GB18030_lacks_none()
    {
        foreach (var name in new[] { "Shift-JIS", "EUC-JP", "GBK", "Big5", "EUC-KR" })
        {
            var codec = TextCodec.ByName(name)!;
            Assert.False(codec.CanEncode('ř'), name);
            var ex = Assert.Throws<UnrepresentableCharactersException>(() => codec.Encode("a ř b"));
            Assert.Equal(1, ex.Report.Total);
        }
        var gb = TextCodec.ByName("GB18030")!;
        Assert.True(gb.CanEncode('ř'));
        Assert.True(gb.CanEncode(0x1F600));
        Assert.Equal("a ř 😀", gb.Decode(gb.Encode("a ř 😀")).Text);
        Assert.Equal("a ? b".Length, TextCodec.ByName("Shift-JIS")!.EncodeLossy("a ř b").Length); // typed into a view that cannot refuse: a question mark
    }

    [Fact]
    public void The_large_file_view_does_not_take_a_multi_byte_encoding_and_the_others_still_work()
    {
        var doc = Core.Documents.Document.FromBytes("abc"u8.ToArray(), PagedBackendFactory.Instance);
        foreach (var codec in TextCodec.All)
            Assert.Equal(codec.Kind != CodecKind.MultiByte, doc.Backend.Supports(codec));
    }
}

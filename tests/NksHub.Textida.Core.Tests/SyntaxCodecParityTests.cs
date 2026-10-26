using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public class SyntaxCodecParityTests
{
    [Fact]
    public void Ascii_fast_path_preserves_every_ascii_byte_and_empty_input()
    {
        byte[] bytes = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        Assert.Equal(TextCodec.Utf8.Decode(bytes).Text, TextCodec.Utf8.GetString(bytes));
        Assert.Equal(TextCodec.Utf8.Decode(bytes).Text, TextCodec.Utf8Bom.GetString(bytes));
        Assert.Equal("", TextCodec.Utf8.GetString([]));
    }

    [Fact]
    public void Unicode_and_malformed_utf8_follow_the_existing_decoder()
    {
        byte[][] inputs =
        [
            "příliš žluťoučký 😀"u8.ToArray(),
            [0xEF, 0xBB, 0xBF, 0x61],
            [0xC3, 0x28, 0x80, 0xFF],
            [0xF0, 0x9F, 0x98],
            [0xED, 0xA0, 0x80],
            [0xF4, 0x90, 0x80, 0x80],
        ];
        foreach (var bytes in inputs)
        {
            string expected = TextCodec.Utf8.Decode(bytes).Text;
            Assert.Equal(expected, TextCodec.Utf8.GetString(bytes));
            Assert.Equal(expected, TextCodec.Utf8Bom.GetString(bytes));
        }
        var random = new Random(1935);
        for (int sample = 0; sample < 200; sample++)
        {
            var bytes = new byte[random.Next(1, 1024)];
            random.NextBytes(bytes);
            Assert.Equal(TextCodec.Utf8.Decode(bytes).Text, TextCodec.Utf8.GetString(bytes));
        }
    }
}

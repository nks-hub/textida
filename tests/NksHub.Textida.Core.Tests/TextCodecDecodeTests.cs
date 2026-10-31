using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>P7.1.5: the decoder that gives a line its text and its map of byte offsets made a string for every character; a line decoded again on every keystroke cost hundreds of kilobytes.</summary>
public class TextCodecDecodeTests
{
    private const string Bad = "�";

    /// <summary>The decoder as it was: one unit at a time, the text of a unit as a string. The map has the offset of the unit for each of its characters.</summary>
    private static (string Text, int[] Map, int[] Invalid) Reference(TextCodec codec, byte[] bytes)
    {
        var text = new StringBuilder();
        var map = new List<int>();
        var invalid = new List<int>();
        int i = codec.Bom.Length > 0 && bytes.AsSpan().StartsWith(codec.Bom) ? codec.Bom.Length : 0;
        void Add(string s, int at, bool bad)
        {
            if (bad) invalid.Add(text.Length);
            foreach (char c in s) { map.Add(at); text.Append(c); }
        }
        while (i < bytes.Length)
        {
            switch (codec.Kind)
            {
                case CodecKind.Utf8:
                    {
                        var status = Rune.DecodeFromUtf8(bytes.AsSpan(i), out Rune r, out int used);
                        bool ok = status == System.Buffers.OperationStatus.Done;
                        if (status == System.Buffers.OperationStatus.NeedMoreData) used = bytes.Length - i;
                        if (used < 1) used = 1;
                        Add(ok ? r.ToString() : Bad, i, !ok);
                        i += used;
                        break;
                    }
                case CodecKind.Utf16:
                    {
                        if (i + 1 >= bytes.Length) { Add(Bad, i, true); i = bytes.Length; break; }
                        char u = Unit16(codec, bytes, i);
                        if (char.IsHighSurrogate(u) && i + 3 < bytes.Length && char.IsLowSurrogate(Unit16(codec, bytes, i + 2))) { Add(new string([u, Unit16(codec, bytes, i + 2)]), i, false); i += 4; }
                        else if (char.IsSurrogate(u)) { Add(Bad, i, true); i += 2; }
                        else { Add(u.ToString(), i, false); i += 2; }
                        break;
                    }
                default:
                    {
                        if (i + 3 >= bytes.Length) { Add(Bad, i, true); i = bytes.Length; break; }
                        uint v = codec.BigEndian
                            ? (uint)(bytes[i] << 24 | bytes[i + 1] << 16 | bytes[i + 2] << 8 | bytes[i + 3])
                            : (uint)(bytes[i + 3] << 24 | bytes[i + 2] << 16 | bytes[i + 1] << 8 | bytes[i]);
                        if (Rune.IsValid(v)) Add(new Rune(v).ToString(), i, false);
                        else Add(Bad, i, true);
                        i += 4;
                        break;
                    }
            }
        }
        map.Add(bytes.Length);
        return (text.ToString(), [.. map], [.. invalid]);
    }

    private static char Unit16(TextCodec codec, byte[] d, int i) => codec.BigEndian ? (char)((d[i] << 8) | d[i + 1]) : (char)((d[i + 1] << 8) | d[i]);

    private static void AssertSame(TextCodec codec, byte[] bytes)
    {
        var expected = Reference(codec, bytes);
        var actual = codec.Decode(bytes);
        Assert.Equal(expected.Text, actual.Text);
        for (int i = 0; i < expected.Map.Length; i++) Assert.Equal(expected.Map[i], actual.ByteOffsetOf(i));
        Assert.Equal(expected.Invalid.Length, actual.InvalidCount);
        foreach (int index in expected.Invalid) Assert.True(actual.IsInvalid(index));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain ascii\r\nsecond line\n")]
    [InlineData("příliš žluťoučký kůň úpěl ďábelské ódy")]
    [InlineData("日本語のテキスト and ascii mixed")]
    [InlineData("emoji 😀 and 𝄞 behind ascii 😀😀")]
    public void Valid_text_decodes_to_the_same_text_and_map_in_every_codec(string text)
    {
        foreach (var codec in TextCodec.All.Where(c => c.Kind is CodecKind.Utf8 or CodecKind.Utf16 or CodecKind.Utf32))
        {
            var bytes = new List<byte>(codec.Bom);
            bytes.AddRange(codec.Encode(text));
            AssertSame(codec, [.. bytes]);
        }
    }

    [Fact]
    public void Invalid_and_truncated_sequences_decode_as_before()
    {
        byte[][] inputs =
        [
            [0xC3, 0x28, 0x80, 0xFF, 0x41],
            [0xF0, 0x9F, 0x98],
            [0xED, 0xA0, 0x80, 0x42],
            [0xF4, 0x90, 0x80, 0x80],
            [0x41, 0x42, 0xE2, 0x82],
            [0x00, 0x00, 0xD8, 0x00, 0x00, 0x41],
        ];
        foreach (var codec in TextCodec.All.Where(c => c.Kind is CodecKind.Utf8 or CodecKind.Utf16 or CodecKind.Utf32))
            foreach (var input in inputs) AssertSame(codec, input);

        var random = new Random(7731);
        for (int sample = 0; sample < 300; sample++)
        {
            var bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            foreach (var codec in TextCodec.All.Where(c => c.Kind is CodecKind.Utf8 or CodecKind.Utf16 or CodecKind.Utf32)) AssertSame(codec, bytes);
        }
    }

    [Fact]
    public void A_text_of_16_KiB_of_valid_utf8_allocates_the_text_and_the_map_and_nothing_per_character()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abc ěščř def 😀 ", 1200)))[..(16 * 1024)];
        bytes = bytes[..TextCodec.Utf8.CompleteLength(bytes)];
        TextCodec.Utf8.Decode(bytes);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = TextCodec.Utf8.Decode(bytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Encoding.UTF8.GetString(bytes), decoded.Text);
        Assert.True(allocated < 8L * bytes.Length, $"{allocated} bytes for {bytes.Length} bytes of text"); // the text and the map are about 5 times the bytes; a string per character was far more
    }
}

using System.Text;
using System.Text.Unicode;
using NksPad.Core.Encodings;

namespace NksPad.Core.Tests;

public class EncodingTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(64)]
    public void ValidTextIsAcceptedForEveryBlockSize(int block)
    {
        var r = new Random(block);
        for (int i = 0; i < 100; i++)
        {
            using var f = new TempFile(Encoding.UTF8.GetBytes(Gen.Mixed(r, r.Next(0, 400))));
            using var mf = MappedFile.Open(f.Path);
            Assert.True(Utf8Validator.IsValid(mf, block));
        }
    }

    [Fact]
    public void ValidatorAgreesWithWholeBufferValidationOnMutatedData()
    {
        var r = new Random(1234);
        for (int i = 0; i < 3000; i++)
        {
            var bytes = Encoding.UTF8.GetBytes(Gen.Mixed(r, r.Next(1, 120)));
            int mutations = r.Next(0, 3);
            for (int m = 0; m < mutations; m++)
                bytes[r.Next(bytes.Length)] = r.Next(3) switch { 0 => (byte)0x80, 1 => (byte)0xFF, _ => (byte)r.Next(256) };
            if (r.Next(5) == 0) bytes = bytes[..r.Next(bytes.Length)];
            using var f = new TempFile(bytes);
            using var mf = MappedFile.Open(f.Path);
            bool expected = Utf8.IsValid(bytes);
            foreach (int block in new[] { 4, 5, 9, 33 })
                Assert.True(expected == Utf8Validator.IsValid(mf, block), $"case {i} block {block}");
        }
    }

    [Theory]
    [InlineData(new byte[] { 0xC0, 0x80 })]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xE2, 0x82 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    public void InvalidSequencesAreRejected(byte[] tail)
    {
        byte[] bytes = [.. "abcdefgh"u8.ToArray(), .. tail];
        using var f = new TempFile(bytes);
        using var mf = MappedFile.Open(f.Path);
        foreach (int block in new[] { 4, 8, 64 })
            Assert.False(Utf8Validator.IsValid(mf, block));
    }

    [Fact]
    public void CompleteLengthDropsTruncatedCharacter()
    {
        var euro = "a€"u8.ToArray();
        Assert.Equal(4, Utf8Validator.CompleteLength(euro));
        Assert.Equal(1, Utf8Validator.CompleteLength(euro[..3]));
        Assert.Equal(1, Utf8Validator.CompleteLength(euro[..2]));
        var emoji = "😀"u8.ToArray();
        for (int cut = 1; cut < 4; cut++) Assert.Equal(0, Utf8Validator.CompleteLength(emoji[..cut]));
        Assert.Equal(4, Utf8Validator.CompleteLength(emoji));
    }

    [Fact]
    public void DetectionCoversBomsUtf8Utf16AndFallback()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        static TextCodec Detect(ReadOnlySpan<byte> sample, bool whole = true) => EncodingGuesser.Guess(sample, whole).Codec;
        Assert.Same(TextCodec.Utf8, Detect("plain"u8));
        Assert.Same(TextCodec.Utf8, Detect("příliš"u8));
        Assert.Same(TextCodec.Utf8Bom, Detect([0xEF, 0xBB, 0xBF, (byte)'a']));
        Assert.Same(TextCodec.Utf16LEBom, Detect([0xFF, 0xFE, (byte)'a', 0]));
        Assert.Same(TextCodec.Utf16BEBom, Detect([0xFE, 0xFF, 0, (byte)'a']));
        Assert.Same(TextCodec.Utf16LE, Detect(Encoding.Unicode.GetBytes("hello world, no bom here")));
        Assert.Same(TextCodec.Utf16BE, Detect(Encoding.BigEndianUnicode.GetBytes("hello world, no bom here")));
        Assert.Same(TextCodec.Cp1250, Detect(Encoding.GetEncoding(1250).GetBytes("žluťoučký kůň")));
    }

    [Fact]
    public void TruncatedSampleStillDetectsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("příliš žluťoučký kůň ", 20)));
        for (int cut = 100; cut < 140; cut++)
            Assert.Same(TextCodec.Utf8, EncodingGuesser.Guess(bytes.AsSpan(0, cut), wholeFile: false).Codec);
    }
}

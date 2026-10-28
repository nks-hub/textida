using NksHub.Textida.Core.History;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.3.1: the patch that keeps a revision of a large file as a few kilobytes beside a whole copy of an earlier one.</summary>
public class BinaryDeltaTests
{
    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new System.Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Splice(byte[] bytes, int at, int remove, params byte[] insert) => [.. bytes[..at], .. insert, .. bytes[(at + remove)..]];

    /// <summary>The patch from <paramref name="source"/> to <paramref name="target"/>, or null when it is longer than <paramref name="max"/>.</summary>
    private static byte[]? Make(byte[] source, byte[] target, long max = long.MaxValue)
    {
        using var s = new TempFile(source);
        using var t = new TempFile(target);
        using var sourceHandle = File.OpenHandle(s.Path);
        using var targetHandle = File.OpenHandle(t.Path);
        using var patch = new MemoryStream();
        long length = BinaryDelta.Create(sourceHandle, source.Length, targetHandle, target.Length, patch, max);
        if (length < 0) return null;
        Assert.Equal(length, patch.Length);
        return patch.ToArray();
    }

    private static byte[] Apply(byte[] source, byte[] patch, long limit = long.MaxValue)
    {
        using var s = new TempFile(source);
        using var sourceHandle = File.OpenHandle(s.Path);
        using var output = new MemoryStream();
        BinaryDelta.Apply(sourceHandle, source.Length, new MemoryStream(patch), output, limit);
        return output.ToArray();
    }

    private static void AssertRoundTrip(byte[] source, byte[] target, long? atMost = null)
    {
        var patch = Make(source, target);
        Assert.NotNull(patch);
        Assert.True(target.AsSpan().SequenceEqual(Apply(source, patch!)), "the patch does not make the target");
        if (atMost is { } most) Assert.True(patch!.Length <= most, $"{patch.Length} bytes");
    }

    [Fact]
    public void FewEditsOfAMegabyteMakeAPatchOfAFewKilobytes()
    {
        byte[] source = Random(1_000_000, 1);
        byte[] target = Splice(source, 100_000, 0, 1, 2, 3, 4, 5);       // an insertion
        target = Splice(target, 400_000, 7000, 9, 9, 9);                // a replaced stretch
        target = Splice(target, 900_000, 0);                            // nothing
        target = [.. target, .. "appended tail"u8.ToArray()];
        AssertRoundTrip(source, target, 20_000);
    }

    [Fact]
    public void TheSameFileIsOneCopyOfAll()
    {
        byte[] source = Random(300_000, 2);
        var patch = Make(source, source)!;
        Assert.InRange(patch.Length, 1, 32);
        Assert.True(source.AsSpan().SequenceEqual(Apply(source, patch)));
    }

    [Fact]
    public void EditsAtTheStartAndAtTheEndAreFound()
    {
        byte[] source = Random(200_000, 3);
        AssertRoundTrip(source, Splice(source, 0, 0, 1, 2, 3), 12_000);                // in front
        AssertRoundTrip(source, Splice(source, 0, 5000), 12_000);                       // the start cut off
        AssertRoundTrip(source, source[..^12_345], 100);                                // the end cut off
        AssertRoundTrip(source, [.. source, .. Random(10_000, 4)], 10_100);             // something appended
    }

    [Fact]
    public void FilesThatAreShorterThanABlockOrEmptyAreMadeToo()
    {
        AssertRoundTrip([], []);
        AssertRoundTrip([], Random(10, 5));
        AssertRoundTrip(Random(10, 6), []);
        AssertRoundTrip(Random(10, 7), Random(20, 8));
        AssertRoundTrip(Random(3000, 9), Random(5000, 10));
        AssertRoundTrip(Random(5000, 11), Random(3000, 12));
    }

    [Fact]
    public void RepeatedContentIsFoundAndNotConfused()
    {
        byte[] source = new byte[3 << 20]; // zeros: every block is the same block
        byte[] target = Splice(source, 1_000_000, 0, 1, 2, 3);
        AssertRoundTrip(source, target, 12_000);

        var pattern = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        byte[] text = [.. Enumerable.Repeat(pattern, 400).SelectMany(p => p)];
        AssertRoundTrip(text, Splice(text, 123_457, 3, 7, 7), 12_000);
    }

    [Fact]
    public void FilesThatAreNothingAlikeMakeNoPatchWithinTheLimit()
    {
        byte[] source = Random(500_000, 13), target = Random(500_000, 14);
        Assert.Null(Make(source, target, 250_000));
        AssertRoundTrip(source, target); // without a limit the patch is the new file and a few bytes
    }

    [Fact]
    public void ABlockSizeThatGrowsWithTheFileKeepsTheIndexSmall()
    {
        Assert.Equal(4096, BinaryDelta.BlockFor(0));
        Assert.Equal(4096, BinaryDelta.BlockFor(1L << 30));
        Assert.Equal(8192, BinaryDelta.BlockFor(2L << 30));
        Assert.Equal(1 << 20, BinaryDelta.BlockFor(long.MaxValue / 4));
    }

    [Fact]
    public void ThePrefixOfTheResultIsTheStartOfTheTarget()
    {
        byte[] source = Random(100_000, 15);
        byte[] target = Splice(source, 50_000, 10, 1, 2, 3);
        var patch = Make(source, target)!;
        Assert.True(target.AsSpan(0, 60_000).SequenceEqual(Apply(source, patch, 60_000)));
        Assert.Empty(Apply(source, patch, 0));
        Assert.True(target.AsSpan().SequenceEqual(Apply(source, patch, long.MaxValue)));
    }

    [Fact]
    public void ADamagedOrTruncatedPatchIsRefused()
    {
        byte[] source = Random(100_000, 16);
        byte[] target = Splice(source, 5000, 0, 1, 2, 3);
        var patch = Make(source, target)!;

        Assert.Throws<InvalidDataException>(() => Apply(source, patch[..^2]));
        Assert.Throws<InvalidDataException>(() => Apply(source, [.. patch[..2], 0x20, .. patch[3..]]));
        Assert.Throws<InvalidDataException>(() => Apply(source, [1, 2, 3, 4, 5]));
        Assert.Throws<InvalidDataException>(() => Apply(source[..50_000], patch)); // the file it was made against is not the one it is applied to
    }
}

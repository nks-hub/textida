namespace NksHub.Textida.Core.Encodings;

/// <summary>
/// A byte buffer decoded for display, with a map from every character to the byte range it came from.
/// Invalid sequences appear as U+FFFD in <see cref="Text"/>; their original bytes stay in <see cref="Bytes"/>.
/// </summary>
public sealed class DecodedText
{
    private readonly int[] _map;      // _map[i] = byte offset where character i starts; _map[Text.Length] = Bytes.Length
    private readonly int[] _invalid;  // sorted indices of characters that stand for invalid byte sequences

    internal DecodedText(TextCodec codec, ReadOnlyMemory<byte> bytes, int bomLength, string text, int[] map, int[] invalid)
    {
        Codec = codec; Bytes = bytes; BomLength = bomLength; Text = text; _map = map; _invalid = invalid;
    }

    public TextCodec Codec { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    /// <summary>Bytes at the start that are a byte order mark and not part of <see cref="Text"/>.</summary>
    public int BomLength { get; }
    public string Text { get; }
    public int InvalidCount => _invalid.Length;

    public bool IsInvalid(int charIndex) => Array.BinarySearch(_invalid, charIndex) >= 0;

    /// <summary>Byte offset of the character at <paramref name="charIndex"/> (0..Text.Length); the second half of a surrogate pair maps to the pair start.</summary>
    public int ByteOffsetOf(int charIndex) => _map[charIndex];

    /// <summary>False inside a surrogate pair, where an edit would cut a character in two.</summary>
    public bool IsCharBoundary(int charIndex) =>
        charIndex <= 0 || charIndex >= Text.Length || _map[charIndex] != _map[charIndex - 1];

    /// <summary>Index of the first character that starts at or after <paramref name="byteOffset"/>.</summary>
    public int CharIndexOf(long byteOffset)
    {
        int lo = 0, hi = _map.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_map[mid] < byteOffset) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>"Re-read as": the same bytes interpreted by another codec. Nothing is written or changed.</summary>
    public DecodedText Reinterpret(TextCodec codec) => codec.Decode(Bytes);
}

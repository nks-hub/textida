using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Hex;

public sealed class BytePattern : ByteQuery
{
    private readonly byte[] values;
    private readonly byte[] masks;

    private BytePattern(byte[] values, byte[] masks)
    {
        if (values.Length == 0) throw new ArgumentException("The search pattern is empty.");
        this.values = values;
        this.masks = masks;
        int start = 0;
        for (int i = 0; i <= masks.Length; i++)
        {
            if (i < masks.Length && masks[i] == 255) continue;
            if (i - start > AnchorLength) (AnchorStart, AnchorLength) = (start, i - start);
            start = i + 1;
        }
    }

    public int Length => values.Length;

    /// <summary>The bytes of the pattern when none of them is a wildcard (a replacement can be written from it); null otherwise.</summary>
    public byte[]? Exact => masks.All(m => m == 255) ? [.. values] : null;
    public override int MinLength => values.Length;
    public override int MaxLength => values.Length;
    internal int AnchorStart { get; }
    internal int AnchorLength { get; }
    internal ReadOnlySpan<byte> Anchor => values.AsSpan(AnchorStart, AnchorLength);

    public static BytePattern FromBytes(ReadOnlySpan<byte> bytes)
    {
        var masks = new byte[bytes.Length];
        Array.Fill(masks, (byte)255);
        return new BytePattern(bytes.ToArray(), masks);
    }

    /// <summary>Pairs of hex digits or '?' nibbles; whitespace between bytes is optional.</summary>
    public static BytePattern ParseHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new List<byte>();
        var masks = new List<byte>();
        int high = -1, highMask = 0;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (high >= 0) throw new FormatException("A hex byte needs two adjacent nibbles.");
                continue;
            }
            int digit = c switch
            {
                '?' => 0,
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => throw new FormatException("Expected a hex digit or '?'."),
            };
            int mask = c == '?' ? 0 : 15;
            if (high < 0) (high, highMask) = (digit, mask);
            else
            {
                values.Add((byte)((high << 4) | digit));
                masks.Add((byte)((highMask << 4) | mask));
                high = -1;
            }
        }
        if (high >= 0) throw new FormatException("A hex byte needs two nibbles.");
        return new BytePattern(values.ToArray(), masks.ToArray());
    }

    /// <summary>Encodes the text without a BOM; unrepresentable characters are rejected by the codec.</summary>
    public static BytePattern FromText(string text, TextCodec codec)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(codec);
        return FromBytes(codec.Encode(text));
    }

    /// <summary>Value, type, optional le/be (default le), e.g. '0x1F40 i32' or '3.14 f64 be'. Types: i/u8/16/32/64, f16/32/64.</summary>
    public static BytePattern ParseNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3) throw new FormatException("Expected a value, type and optional le/be byte order.");
        bool bigEndian = parts.Length == 3 && parts[2].Equals("be", StringComparison.OrdinalIgnoreCase);
        if (parts.Length == 3 && !bigEndian && !parts[2].Equals("le", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Byte order must be le or be.");
        string type = parts[1].ToLowerInvariant();
        int bits = type switch
        {
            "i8" or "u8" => 8,
            "i16" or "u16" or "f16" => 16,
            "i32" or "u32" or "f32" => 32,
            "i64" or "u64" or "f64" => 64,
            _ => throw new FormatException("Unknown numeric type."),
        };
        ulong value;
        if (type[0] == 'f') value = FloatBits(parts[0], bits);
        else
        {
            BigInteger number = Integer(parts[0]);
            bool signed = type[0] == 'i';
            BigInteger min = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
            BigInteger max = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
            if (number < min || number > max) throw new OverflowException("The value does not fit the numeric type.");
            if (number.Sign < 0) number += BigInteger.One << bits;
            value = (ulong)number;
        }
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        bytes = bytes[..(bits / 8)];
        if (bigEndian) bytes.Reverse();
        return FromBytes(bytes);
    }

    private static BigInteger Integer(string text)
    {
        bool negative = text.StartsWith('-');
        string magnitude = text.StartsWith('-') || text.StartsWith('+') ? text[1..] : text;
        if (!magnitude.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (magnitude.Length == 2) throw new FormatException("A hexadecimal value needs digits.");
        var value = BigInteger.Parse("0" + magnitude[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    private static ulong FloatBits(string text, int bits)
    {
        switch (bits)
        {
            case 16:
                Half half = Half.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (!Half.IsFinite(half)) throw new OverflowException("Expected a finite float16 value.");
                return BitConverter.HalfToUInt16Bits(half);
            case 32:
                float single = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (!float.IsFinite(single)) throw new OverflowException("Expected a finite float32 value.");
                return BitConverter.SingleToUInt32Bits(single);
            default:
                double number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (!double.IsFinite(number)) throw new OverflowException("Expected a finite float64 value.");
                return BitConverter.DoubleToUInt64Bits(number);
        }
    }

    /// <summary>One syntax node per byte, for building a <see cref="ByteRegex"/> out of hex text.</summary>
    internal IEnumerable<ByteRegex.Node> ToNodes() => values.Select((v, i) => ByteRegex.SetFor(v, masks[i]));

    internal override bool Scan(ReadOnlySpan<byte> window, int starts, long block, List<ByteRange> matches, int maxResults, CancellationToken ct)
    {
        for (int at = 0; at < starts; at++)
        {
            if ((at & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (AnchorLength > 0)
            {
                int found = window.Slice(at + AnchorStart, starts - at + AnchorLength - 1).IndexOf(Anchor);
                if (found < 0) break;
                at += found;
                ct.ThrowIfCancellationRequested();
            }
            if (!Matches(window.Slice(at, Length))) continue;
            matches.Add(new ByteRange(block + at, block + at + Length));
            if (matches.Count == maxResults) return true;
        }
        return false;
    }

    internal bool Matches(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < values.Length; i++)
            if ((bytes[i] & masks[i]) != values[i]) return false;
        return true;
    }
}

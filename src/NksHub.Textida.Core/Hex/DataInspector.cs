using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace NksHub.Textida.Core.Hex;

public enum InspectorKind
{
    Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64,
    Uleb128, Sleb128,
    Float16, Float32, Float64,
    UnixTime32, UnixTime64, FileTime, DotNetTicks,
    Latin1, Utf8, Utf16, Utf32,
    Guid,
}

public enum InspectorStatus
{
    Ok,
    /// <summary>The data ends before the value does; <see cref="InspectorValue.Text"/> is empty. It is never shown as zero.</summary>
    Incomplete,
    /// <summary>The bytes are not a value of the type (bad UTF-8, a date outside the calendar, an overlong LEB128).</summary>
    Invalid,
}

/// <param name="Length">Bytes the value takes: for a value of variable length those it used, for an incomplete one those it would need (at least).</param>
public sealed record InspectorValue(InspectorKind Kind, InspectorStatus Status, int Length, string Text);

/// <summary>Reads and writes the typed values of the hex data inspector; pure functions over bytes, no document.</summary>
public static class DataInspector
{
    public static IReadOnlyList<InspectorKind> All { get; } = Enum.GetValues<InspectorKind>();

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Fixed size of a kind in bytes; 0 for the kinds whose size depends on the data (LEB128 and the character kinds).</summary>
    public static int SizeOf(InspectorKind kind) => kind switch
    {
        InspectorKind.Int8 or InspectorKind.UInt8 => 1,
        InspectorKind.Int16 or InspectorKind.UInt16 or InspectorKind.Float16 => 2,
        InspectorKind.Int32 or InspectorKind.UInt32 or InspectorKind.Float32 or InspectorKind.UnixTime32 => 4,
        InspectorKind.Int64 or InspectorKind.UInt64 or InspectorKind.Float64 or InspectorKind.UnixTime64 or InspectorKind.FileTime or InspectorKind.DotNetTicks => 8,
        InspectorKind.Guid => 16,
        InspectorKind.Latin1 => 1,
        _ => 0,
    };

    public static InspectorValue Decode(InspectorKind kind, ReadOnlySpan<byte> data, bool bigEndian)
    {
        int size = SizeOf(kind);
        if (size > 0 && data.Length < size) return new(kind, InspectorStatus.Incomplete, size, "");
        InspectorValue Ok(string text, int length = 0) => new(kind, InspectorStatus.Ok, length > 0 ? length : size, text);
        InspectorValue Bad(int length = 0) => new(kind, InspectorStatus.Invalid, length > 0 ? length : size, "");
        var d = data;
        switch (kind)
        {
            case InspectorKind.Int8: return Ok(((sbyte)d[0]).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.UInt8: return Ok(d[0].ToString(CultureInfo.InvariantCulture));
            case InspectorKind.Int16: return Ok((bigEndian ? BinaryPrimitives.ReadInt16BigEndian(d) : BinaryPrimitives.ReadInt16LittleEndian(d)).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.UInt16: return Ok((bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(d) : BinaryPrimitives.ReadUInt16LittleEndian(d)).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.Int32: return Ok(I32(d, bigEndian).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.UInt32: return Ok((bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(d) : BinaryPrimitives.ReadUInt32LittleEndian(d)).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.Int64: return Ok(I64(d, bigEndian).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.UInt64: return Ok((bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(d) : BinaryPrimitives.ReadUInt64LittleEndian(d)).ToString(CultureInfo.InvariantCulture));
            case InspectorKind.Float16: return Ok((bigEndian ? BinaryPrimitives.ReadHalfBigEndian(d) : BinaryPrimitives.ReadHalfLittleEndian(d)).ToString("R", CultureInfo.InvariantCulture));
            case InspectorKind.Float32: return Ok((bigEndian ? BinaryPrimitives.ReadSingleBigEndian(d) : BinaryPrimitives.ReadSingleLittleEndian(d)).ToString("R", CultureInfo.InvariantCulture));
            case InspectorKind.Float64: return Ok((bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(d) : BinaryPrimitives.ReadDoubleLittleEndian(d)).ToString("R", CultureInfo.InvariantCulture));
            case InspectorKind.UnixTime32: return Time(UnixEpoch, I32(d, bigEndian) * TimeSpan.TicksPerSecond, kind, size);
            case InspectorKind.UnixTime64:
            {
                long seconds = I64(d, bigEndian);
                return seconds is < -62135596800L or > 253402300799L ? Bad() : Time(UnixEpoch, seconds * TimeSpan.TicksPerSecond, kind, size);
            }
            case InspectorKind.FileTime: return I64(d, bigEndian) < 0 ? Bad() : Time(new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc), I64(d, bigEndian), kind, size);
            case InspectorKind.DotNetTicks: return Time(DateTime.MinValue, I64(d, bigEndian), kind, size);
            case InspectorKind.Guid: return Ok(new Guid(d[..16], bigEndian).ToString());
            case InspectorKind.Latin1: return Ok(Show((char)d[0]));
            case InspectorKind.Uleb128 or InspectorKind.Sleb128: return Leb(kind, data);
            case InspectorKind.Utf8: return Utf8(data);
            case InspectorKind.Utf16: return Utf16(data, bigEndian);
            case InspectorKind.Utf32:
            {
                if (data.Length < 4) return new(kind, InspectorStatus.Incomplete, 4, "");
                uint v = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(d) : BinaryPrimitives.ReadUInt32LittleEndian(d);
                return Rune.IsValid(v) ? new(kind, InspectorStatus.Ok, 4, Show(new Rune((int)v))) : new(kind, InspectorStatus.Invalid, 4, "");
            }
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static int I32(ReadOnlySpan<byte> d, bool be) => be ? BinaryPrimitives.ReadInt32BigEndian(d) : BinaryPrimitives.ReadInt32LittleEndian(d);
    private static long I64(ReadOnlySpan<byte> d, bool be) => be ? BinaryPrimitives.ReadInt64BigEndian(d) : BinaryPrimitives.ReadInt64LittleEndian(d);

    private static InspectorValue Time(DateTime origin, long ticks, InspectorKind kind, int size)
    {
        try { return new(kind, InspectorStatus.Ok, size, origin.AddTicks(ticks).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.')); }
        catch (ArgumentOutOfRangeException) { return new(kind, InspectorStatus.Invalid, size, ""); }
    }

    private static string Show(char c) => Show(new Rune(c));

    private static string Show(Rune r) => (Rune.IsControl(r) ? "·" : r.ToString()) + $" U+{r.Value:X4}";

    private static InspectorValue Leb(InspectorKind kind, ReadOnlySpan<byte> data)
    {
        bool signed = kind == InspectorKind.Sleb128;
        BigInteger value = BigInteger.Zero;
        int shift = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (i >= 10) return new(kind, InspectorStatus.Invalid, 10, "");
            byte b = data[i];
            value |= (BigInteger)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) != 0) continue;
            if (signed && (b & 0x40) != 0) value -= BigInteger.One << shift;
            bool fits = signed ? value >= long.MinValue && value <= long.MaxValue : value <= ulong.MaxValue;
            return fits ? new(kind, InspectorStatus.Ok, i + 1, value.ToString(CultureInfo.InvariantCulture)) : new(kind, InspectorStatus.Invalid, i + 1, "");
        }
        return new(kind, InspectorStatus.Incomplete, data.Length + 1, "");
    }

    private static InspectorValue Utf8(ReadOnlySpan<byte> data)
    {
        const InspectorKind kind = InspectorKind.Utf8;
        if (data.Length == 0) return new(kind, InspectorStatus.Incomplete, 1, "");
        int need = data[0] switch { < 0x80 => 1, >= 0xC2 and < 0xE0 => 2, >= 0xE0 and < 0xF0 => 3, >= 0xF0 and < 0xF5 => 4, _ => 0 };
        if (need == 0) return new(kind, InspectorStatus.Invalid, 1, "");
        int have = Math.Min(need, data.Length);
        var status = Rune.DecodeFromUtf8(data[..have], out var rune, out int used);
        return status switch
        {
            System.Buffers.OperationStatus.Done => new(kind, InspectorStatus.Ok, used, Show(rune)),
            System.Buffers.OperationStatus.NeedMoreData => new(kind, InspectorStatus.Incomplete, need, ""),
            _ => new(kind, InspectorStatus.Invalid, Math.Max(1, used), ""),
        };
    }

    private static InspectorValue Utf16(ReadOnlySpan<byte> data, bool be)
    {
        const InspectorKind kind = InspectorKind.Utf16;
        if (data.Length < 2) return new(kind, InspectorStatus.Incomplete, 2, "");
        char first = (char)(be ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data));
        if (!char.IsSurrogate(first)) return new(kind, InspectorStatus.Ok, 2, Show(first));
        if (char.IsLowSurrogate(first)) return new(kind, InspectorStatus.Invalid, 2, "");
        if (data.Length < 4) return new(kind, InspectorStatus.Incomplete, 4, "");
        char second = (char)(be ? BinaryPrimitives.ReadUInt16BigEndian(data[2..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));
        return char.IsLowSurrogate(second) ? new(kind, InspectorStatus.Ok, 4, Show(new Rune(first, second))) : new(kind, InspectorStatus.Invalid, 2, "");
    }

    /// <summary>
    /// Bytes of <paramref name="text"/> as a value of the kind. False with a reason (<paramref name="error"/>) when the text is not a value of the
    /// type or does not fit its range; nothing is clamped or rounded to fit.
    /// </summary>
    public static bool TryEncode(InspectorKind kind, string text, bool bigEndian, out byte[] bytes, out string error)
    {
        bytes = [];
        error = "";
        text = text.Trim();
        if (text.Length == 0) { error = "empty"; return false; }
        try
        {
            switch (kind)
            {
                case InspectorKind.Int8: case InspectorKind.Int16: case InspectorKind.Int32: case InspectorKind.Int64:
                case InspectorKind.UInt8: case InspectorKind.UInt16: case InspectorKind.UInt32: case InspectorKind.UInt64:
                {
                    int bits = SizeOf(kind) * 8;
                    bool signed = (int)kind % 2 == 0;
                    var n = ParseInteger(text);
                    var min = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
                    var max = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
                    if (n < min || n > max) { error = $"range {min}..{max}"; return false; }
                    bytes = Integer(n, bits / 8, bigEndian);
                    return true;
                }
                case InspectorKind.Uleb128 or InspectorKind.Sleb128:
                {
                    var n = ParseInteger(text);
                    bool signed = kind == InspectorKind.Sleb128;
                    var min = signed ? (BigInteger)long.MinValue : BigInteger.Zero;
                    var max = signed ? (BigInteger)long.MaxValue : ulong.MaxValue;
                    if (n < min || n > max) { error = $"range {min}..{max}"; return false; }
                    bytes = EncodeLeb(n, signed);
                    return true;
                }
                case InspectorKind.Float16:
                {
                    var v = Half.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!Half.IsFinite(v)) { error = "range"; return false; }
                    bytes = new byte[2];
                    if (bigEndian) BinaryPrimitives.WriteHalfBigEndian(bytes, v); else BinaryPrimitives.WriteHalfLittleEndian(bytes, v);
                    return true;
                }
                case InspectorKind.Float32:
                {
                    var v = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!float.IsFinite(v)) { error = "range"; return false; }
                    bytes = new byte[4];
                    if (bigEndian) BinaryPrimitives.WriteSingleBigEndian(bytes, v); else BinaryPrimitives.WriteSingleLittleEndian(bytes, v);
                    return true;
                }
                case InspectorKind.Float64:
                {
                    var v = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(v)) { error = "range"; return false; }
                    bytes = new byte[8];
                    if (bigEndian) BinaryPrimitives.WriteDoubleBigEndian(bytes, v); else BinaryPrimitives.WriteDoubleLittleEndian(bytes, v);
                    return true;
                }
                case InspectorKind.UnixTime32 or InspectorKind.UnixTime64 or InspectorKind.FileTime or InspectorKind.DotNetTicks:
                {
                    if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)) { error = "date"; return false; }
                    long ticks = kind switch
                    {
                        InspectorKind.UnixTime32 or InspectorKind.UnixTime64 => (when - UnixEpoch).Ticks / TimeSpan.TicksPerSecond,
                        InspectorKind.FileTime => (when - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks,
                        _ => when.Ticks,
                    };
                    if (kind == InspectorKind.UnixTime32 && (ticks < int.MinValue || ticks > int.MaxValue)) { error = $"range {int.MinValue}..{int.MaxValue}"; return false; }
                    if (kind == InspectorKind.FileTime && ticks < 0) { error = "range"; return false; }
                    bytes = Integer(ticks, SizeOf(kind), bigEndian);
                    return true;
                }
                case InspectorKind.Guid:
                    if (!Guid.TryParse(text, out var guid)) { error = "guid"; return false; }
                    bytes = new byte[16];
                    guid.TryWriteBytes(bytes, bigEndian, out _);
                    return true;
                case InspectorKind.Latin1 or InspectorKind.Utf8 or InspectorKind.Utf16 or InspectorKind.Utf32:
                {
                    if (!TryScalar(text, out var rune)) { error = "character"; return false; }
                    switch (kind)
                    {
                        case InspectorKind.Latin1:
                            if (rune.Value > 0xFF) { error = "range 0..255"; return false; }
                            bytes = [(byte)rune.Value];
                            break;
                        case InspectorKind.Utf8:
                            bytes = new byte[rune.Utf8SequenceLength];
                            rune.EncodeToUtf8(bytes);
                            break;
                        case InspectorKind.Utf16:
                            bytes = new byte[rune.Utf16SequenceLength * 2];
                            Span<char> units = stackalloc char[2];
                            rune.EncodeToUtf16(units);
                            for (int i = 0; i < rune.Utf16SequenceLength; i++)
                                if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), units[i]); else BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), units[i]);
                            break;
                        default:
                            bytes = Integer(rune.Value, 4, bigEndian);
                            break;
                    }
                    return true;
                }
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            error = "format";
            return false;
        }
    }

    private static BigInteger ParseInteger(string text)
    {
        bool negative = text.StartsWith('-');
        string magnitude = negative || text.StartsWith('+') ? text[1..] : text;
        if (!magnitude.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (magnitude.Length == 2) throw new FormatException();
        var value = BigInteger.Parse("0" + magnitude[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    private static byte[] Integer(BigInteger n, int size, bool bigEndian)
    {
        if (n.Sign < 0) n += BigInteger.One << (size * 8);
        var all = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(all, (ulong)n);
        var bytes = all[..size];
        if (bigEndian) Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] EncodeLeb(BigInteger n, bool signed)
    {
        var bytes = new List<byte>();
        while (true)
        {
            byte b = (byte)(n & 0x7F);
            n >>= 7;
            bool done = signed ? (n.IsZero && (b & 0x40) == 0) || (n == BigInteger.MinusOne && (b & 0x40) != 0) : n.IsZero;
            bytes.Add(done ? b : (byte)(b | 0x80));
            if (done) return [.. bytes];
        }
    }

    private static bool TryScalar(string text, out Rune rune)
    {
        rune = default;
        if (text.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp) && Rune.TryCreate(cp, out rune);
        var e = text.EnumerateRunes();
        if (!e.MoveNext()) return false;
        rune = e.Current;
        return !e.MoveNext();
    }
}

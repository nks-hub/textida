using System.Text;

namespace NksPad.Core.Encodings;

public enum CodecKind { Utf8, Utf16, Utf32, SingleByte }

/// <summary>
/// The one codec layer of the editor (it replaced a second, windowed-only codec of the document model). It serves two
/// callers: whole-buffer decoding with a char-to-byte map (<see cref="Decode(ReadOnlyMemory{byte})"/>) for loss checks,
/// conversion and normalisation, and a windowed unit API (<see cref="ReadUnit"/>, <see cref="IsBoundary"/>) for backends
/// that must find character boundaries without decoding a file. Both walk the same units: an invalid sequence is one
/// unit that shows as one U+FFFD while its bytes stay untouched, and only edited ranges are ever encoded again.
/// Unlike <see cref="Encoding"/> it never substitutes characters silently: unrepresentable output is reported to the caller.
/// </summary>
public sealed class TextCodec
{
    internal const string Bad = "�";

    private readonly char[]? _toChar;                  // single-byte: byte -> char
    private readonly Dictionary<char, byte>? _toByte;    // single-byte: char -> byte

    public string Name { get; }
    public CodecKind Kind { get; }
    public bool BigEndian { get; }
    /// <summary>Byte order mark written/stripped by this codec variant; empty when it has none.</summary>
    public byte[] Bom { get; }
    /// <summary>Windows code page number, or 0 for tables that do not have one in .NET.</summary>
    public int CodePage { get; }

    /// <summary>True when the target cannot represent every Unicode character (any single-byte page).</summary>
    public bool IsLossy => Kind == CodecKind.SingleByte;

    private TextCodec(string name, CodecKind kind, bool bigEndian, byte[] bom, int codePage, char[]? table)
    {
        Name = name; Kind = kind; BigEndian = bigEndian; Bom = bom; CodePage = codePage;
        if (table is null) return;
        _toChar = table;
        _toByte = new Dictionary<char, byte>(256);
        for (int i = 255; i >= 0; i--) _toByte[table[i]] = (byte)i; // lowest byte wins for duplicates
    }

    public static readonly TextCodec Utf8 = new("UTF-8", CodecKind.Utf8, false, [], 65001, null);
    public static readonly TextCodec Utf8Bom = new("UTF-8 with BOM", CodecKind.Utf8, false, [0xEF, 0xBB, 0xBF], 65001, null);
    public static readonly TextCodec Utf16LE = new("UTF-16 LE", CodecKind.Utf16, false, [], 1200, null);
    public static readonly TextCodec Utf16LEBom = new("UTF-16 LE with BOM", CodecKind.Utf16, false, [0xFF, 0xFE], 1200, null);
    public static readonly TextCodec Utf16BE = new("UTF-16 BE", CodecKind.Utf16, true, [], 1201, null);
    public static readonly TextCodec Utf16BEBom = new("UTF-16 BE with BOM", CodecKind.Utf16, true, [0xFE, 0xFF], 1201, null);
    public static readonly TextCodec Utf32LE = new("UTF-32 LE", CodecKind.Utf32, false, [], 12000, null);
    public static readonly TextCodec Utf32LEBom = new("UTF-32 LE with BOM", CodecKind.Utf32, false, [0xFF, 0xFE, 0, 0], 12000, null);
    public static readonly TextCodec Utf32BE = new("UTF-32 BE", CodecKind.Utf32, true, [], 12001, null);
    public static readonly TextCodec Utf32BEBom = new("UTF-32 BE with BOM", CodecKind.Utf32, true, [0, 0, 0xFE, 0xFF], 12001, null);
    public static readonly TextCodec Cp1250 = new("Windows-1250", CodecKind.SingleByte, false, [], 1250, FromDotNet(1250));
    public static readonly TextCodec Cp1252 = new("Windows-1252", CodecKind.SingleByte, false, [], 1252, FromDotNet(1252));
    public static readonly TextCodec Latin2 = new("ISO-8859-2", CodecKind.SingleByte, false, [], 28592, FromDotNet(28592));
    public static readonly TextCodec Kamenicky = new("Kamenický (CP895)", CodecKind.SingleByte, false, [], 0, KamenickyTable.Build());

    public static IReadOnlyList<TextCodec> All { get; } =
    [
        Utf8, Utf8Bom, Utf16LE, Utf16LEBom, Utf16BE, Utf16BEBom, Utf32LE, Utf32LEBom, Utf32BE, Utf32BEBom,
        Cp1250, Cp1252, Latin2, Kamenicky,
    ];

    public static TextCodec? ByName(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The same encoding with or without a byte order mark; single-byte codecs return themselves.</summary>
    public TextCodec WithBom(bool bom) => Kind switch
    {
        CodecKind.Utf8 => bom ? Utf8Bom : Utf8,
        CodecKind.Utf16 => BigEndian ? (bom ? Utf16BEBom : Utf16BE) : (bom ? Utf16LEBom : Utf16LE),
        CodecKind.Utf32 => BigEndian ? (bom ? Utf32BEBom : Utf32BE) : (bom ? Utf32LEBom : Utf32LE),
        _ => this,
    };

    /// <summary>True when both codecs map characters to the same bytes and differ at most in the byte order mark.</summary>
    public bool SameBytesAs(TextCodec other) => Kind == other.Kind && BigEndian == other.BigEndian && CodePage == other.CodePage;

    public override string ToString() => Name;

    // .NET ships the code page data in the shared runtime, so the provider works in trimmed and NativeAOT apps.
    private static char[] FromDotNet(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var table = new char[256];
        var one = new byte[1];
        for (int b = 0; b < 256; b++)
        {
            one[0] = (byte)b;
            try { table[b] = enc.GetString(one)[0]; }
            catch (DecoderFallbackException) { table[b] = (char)b; } // undefined slot: keep as a C1 control so it round-trips
        }
        return table;
    }

    // ---- Decoding ----

    /// <summary>Decodes the whole buffer. A BOM is consumed only when this codec variant has one.</summary>
    public DecodedText Decode(ReadOnlyMemory<byte> bytes) => DecodeInBlocks(bytes, int.MaxValue);

    /// <summary>
    /// Decodes in blocks cut at character boundaries; the result is identical to <see cref="Decode"/> for every block size.
    /// </summary>
    public DecodedText DecodeInBlocks(ReadOnlyMemory<byte> bytes, int blockSize)
    {
        if (blockSize < 4) throw new ArgumentOutOfRangeException(nameof(blockSize));
        var span = bytes.Span;
        int bom = Bom.Length > 0 && span.StartsWith(Bom) ? Bom.Length : 0;
        var text = new StringBuilder(span.Length);
        var map = new List<int>(span.Length + 1);
        var invalid = new List<int>();
        int pos = bom;
        while (pos < span.Length)
        {
            int end = (int)Math.Min((long)pos + blockSize, span.Length);
            int cut = end == span.Length ? end - pos : CompleteLength(span[pos..end]);
            if (cut == 0) cut = end - pos;
            DecodeCore(span.Slice(pos, cut), pos, text, map, invalid);
            pos += cut;
        }
        map.Add(span.Length);
        return new DecodedText(this, bytes, bom, text.ToString(), map.ToArray(), invalid.ToArray());
    }

    /// <summary>Length of the longest prefix that does not end inside a character.</summary>
    public int CompleteLength(ReadOnlySpan<byte> data)
    {
        switch (Kind)
        {
            case CodecKind.Utf8: return Utf8Validator.CompleteLength(data);
            case CodecKind.Utf32: return data.Length & ~3;
            case CodecKind.Utf16:
                int n = data.Length & ~1;
                if (n >= 2 && char.IsHighSurrogate(ReadUnit(data, n - 2))) n -= 2;
                return n;
            default: return data.Length;
        }
    }

    private char ReadUnit(ReadOnlySpan<byte> d, int i) =>
        BigEndian ? (char)((d[i] << 8) | d[i + 1]) : (char)((d[i + 1] << 8) | d[i]);

    private void DecodeCore(ReadOnlySpan<byte> d, int baseOffset, StringBuilder text, List<int>? map, List<int>? invalid)
    {
        void Add(string s, int at, bool bad)
        {
            if (bad) invalid?.Add(text.Length);
            foreach (char c in s) { map?.Add(baseOffset + at); text.Append(c); }
        }
        int i = 0;
        switch (Kind)
        {
            case CodecKind.Utf8:
                while (i < d.Length)
                {
                    var status = Rune.DecodeFromUtf8(d[i..], out Rune r, out int used);
                    bool ok = status == System.Buffers.OperationStatus.Done;
                    if (status == System.Buffers.OperationStatus.NeedMoreData) used = d.Length - i;
                    if (used < 1) used = 1;
                    Add(ok ? r.ToString() : Bad, i, !ok);
                    i += used;
                }
                break;
            case CodecKind.Utf16:
                while (i < d.Length)
                {
                    if (i + 1 >= d.Length) { Add(Bad, i, true); break; }
                    char u = ReadUnit(d, i);
                    if (char.IsHighSurrogate(u) && i + 3 < d.Length && char.IsLowSurrogate(ReadUnit(d, i + 2)))
                    { Add(new string([u, ReadUnit(d, i + 2)]), i, false); i += 4; }
                    else if (char.IsSurrogate(u)) { Add(Bad, i, true); i += 2; }
                    else { Add(u.ToString(), i, false); i += 2; }
                }
                break;
            case CodecKind.Utf32:
                while (i < d.Length)
                {
                    if (i + 3 >= d.Length) { Add(Bad, i, true); break; }
                    uint v = BigEndian
                        ? (uint)(d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3])
                        : (uint)(d[i + 3] << 24 | d[i + 2] << 16 | d[i + 1] << 8 | d[i]);
                    if (Rune.IsValid(v)) Add(new Rune(v).ToString(), i, false);
                    else Add(Bad, i, true);
                    i += 4;
                }
                break;
            default:
                for (; i < d.Length; i++) { map?.Add(baseOffset + i); text.Append(_toChar![d[i]]); }
                break;
        }
    }

    // ---- Windowed unit API (no whole-file decode) ----

    /// <summary>Windows handed to the boundary methods must start at a multiple of this (relative to the end of the BOM).</summary>
    public int Alignment => Kind switch { CodecKind.Utf16 => 2, CodecKind.Utf32 => 4, _ => 1 };

    /// <summary>Length of this codec's BOM when <paramref name="bytes"/> starts with it, else 0.</summary>
    public int BomLengthIn(ReadOnlySpan<byte> bytes) => Bom.Length > 0 && bytes.StartsWith(Bom) ? Bom.Length : 0;

    /// <summary>Decodes a fragment that starts and ends on unit boundaries into the same text units as <see cref="Decode(ReadOnlyMemory{byte})"/>.</summary>
    public string GetString(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        DecodeCore(bytes, 0, text, null, null);
        return text.ToString();
    }

    public int CountChars(ReadOnlySpan<byte> bytes) => Kind switch
    {
        CodecKind.Utf8 => Encoding.UTF8.GetCharCount(bytes),
        CodecKind.SingleByte => bytes.Length,
        _ => GetString(bytes).Length,
    };

    /// <summary>Reads the unit at the start of <paramref name="data"/> (which begins on a boundary) and returns its byte length.</summary>
    public int ReadUnit(ReadOnlySpan<byte> data, out int chars)
    {
        chars = 1;
        if (data.IsEmpty) return 0;
        switch (Kind)
        {
            case CodecKind.Utf8:
                if (data[0] < 0x80) return 1;
                Rune.DecodeFromUtf8(data, out var rune, out int used);
                chars = rune.Utf16SequenceLength;
                return Math.Max(1, used);
            case CodecKind.SingleByte:
                return 1;
            case CodecKind.Utf16:
                if (data.Length < 2) return data.Length;
                if (char.IsHighSurrogate(ReadUnit(data, 0)) && data.Length >= 4 && char.IsLowSurrogate(ReadUnit(data, 2)))
                {
                    chars = 2;
                    return 4;
                }
                return 2;
            default:
                if (data.Length < 4) return data.Length;
                if (Rune.IsValid(ReadU32(data, 0))) chars = new Rune(ReadU32(data, 0)).Utf16SequenceLength;
                return 4;
        }
    }

    private uint ReadU32(ReadOnlySpan<byte> d, int at) =>
        BigEndian ? (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]) : (uint)(d[at + 3] << 24 | d[at + 2] << 16 | d[at + 1] << 8 | d[at]);

    /// <summary>
    /// True when <paramref name="pos"/> starts a unit. <paramref name="window"/> must hold the bytes around it:
    /// at least 3 before (or up to the first editable byte) and 4 after (or up to the end of the document).
    /// </summary>
    public bool IsBoundary(ReadOnlySpan<byte> window, int pos)
    {
        if (pos <= 0 || pos >= window.Length) return true;
        switch (Kind)
        {
            case CodecKind.SingleByte:
                return true;
            case CodecKind.Utf8:
                if ((window[pos] & 0xC0) != 0x80) return true;
                int lead = pos - 1, floor = Math.Max(0, pos - 3);
                while (lead >= floor && (window[lead] & 0xC0) == 0x80) lead--;
                if (lead < floor) return true;
                int i = lead;
                while (i < pos) i += ReadUnit(window[i..], out _);
                return i == pos;
            case CodecKind.Utf16:
                if ((pos & 1) != 0) return false;
                return !(pos >= 2 && pos + 2 <= window.Length && char.IsHighSurrogate(ReadUnit(window, pos - 2)) && char.IsLowSurrogate(ReadUnit(window, pos)));
            default:
                return (pos & 3) == 0;
        }
    }

    /// <summary>True when <paramref name="pos"/> lies between the CR and the LF of one CRLF terminator.</summary>
    public bool IsInsideCrLf(ReadOnlySpan<byte> window, int pos)
    {
        int w = Alignment;
        if (pos < w || pos + w > window.Length) return false;
        return Kind switch
        {
            CodecKind.Utf16 => ReadUnit(window, pos - 2) == 13 && ReadUnit(window, pos) == 10,
            CodecKind.Utf32 => ReadU32(window, pos - 4) == 13 && ReadU32(window, pos) == 10,
            _ => window[pos - 1] == 13 && window[pos] == 10,
        };
    }

    // ---- Encoding ----

    /// <summary>True when <paramref name="codePoint"/> can be written (lone surrogates never can).</summary>
    public bool CanEncode(int codePoint)
    {
        if (!Rune.IsValid(codePoint)) return false;
        return Kind != CodecKind.SingleByte || (codePoint <= 0xFFFF && _toByte!.ContainsKey((char)codePoint));
    }

    /// <summary>Appends the bytes of one code point; the caller has checked <see cref="CanEncode"/>.</summary>
    internal void EncodeRune(int codePoint, ICollection<byte> output)
    {
        var rune = new Rune(codePoint);
        switch (Kind)
        {
            case CodecKind.Utf8:
                Span<byte> b8 = stackalloc byte[4];
                int n8 = rune.EncodeToUtf8(b8);
                for (int i = 0; i < n8; i++) output.Add(b8[i]);
                break;
            case CodecKind.Utf16:
                Span<char> c16 = stackalloc char[2];
                int n16 = rune.EncodeToUtf16(c16);
                for (int i = 0; i < n16; i++)
                {
                    byte lo = (byte)c16[i], hi = (byte)(c16[i] >> 8);
                    if (BigEndian) { output.Add(hi); output.Add(lo); } else { output.Add(lo); output.Add(hi); }
                }
                break;
            case CodecKind.Utf32:
                if (BigEndian) { output.Add((byte)(codePoint >> 24)); output.Add((byte)(codePoint >> 16)); output.Add((byte)(codePoint >> 8)); output.Add((byte)codePoint); }
                else { output.Add((byte)codePoint); output.Add((byte)(codePoint >> 8)); output.Add((byte)(codePoint >> 16)); output.Add((byte)(codePoint >> 24)); }
                break;
            default:
                output.Add(_toByte![(char)codePoint]);
                break;
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/> without the BOM. Throws <see cref="UnrepresentableCharactersException"/>
    /// when a character does not exist in this encoding; nothing is ever replaced silently.
    /// </summary>
    public byte[] Encode(string text)
    {
        var result = new List<byte>(text.Length);
        for (int i = 0; i < text.Length;)
        {
            bool ok = Rune.DecodeFromUtf16(text.AsSpan(i), out Rune r, out int used) == System.Buffers.OperationStatus.Done;
            int cp = ok ? r.Value : text[i];
            if (!CanEncode(cp)) throw new UnrepresentableCharactersException(LossReport.ForText(text, this));
            EncodeRune(cp, result);
            i += used;
        }
        return result.ToArray();
    }

    /// <summary>Length of <see cref="EncodeLossy"/> without building it.</summary>
    public int LossyByteCount(ReadOnlySpan<char> text)
    {
        switch (Kind)
        {
            case CodecKind.Utf8: return Encoding.UTF8.GetByteCount(text);
            case CodecKind.Utf16: return text.Length * 2;
            case CodecKind.Utf32:
                int runes = 0;
                for (int i = 0; i < text.Length; i++)
                    if (!(char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))) runes++;
                    else { runes++; i++; }
                return runes * 4;
            default: return text.Length;
        }
    }

    /// <summary>
    /// Encodes text typed into a view that cannot refuse it: a character this encoding cannot hold becomes '?'
    /// (U+FFFD in the Unicode encodings), once per UTF-16 unit. Saving and converting never use this; they go through <see cref="ByteFidelity"/>.
    /// </summary>
    public byte[] EncodeLossy(ReadOnlySpan<char> text)
    {
        var result = new List<byte>(text.Length);
        for (int i = 0; i < text.Length;)
        {
            bool ok = Rune.DecodeFromUtf16(text[i..], out Rune r, out int used) == System.Buffers.OperationStatus.Done;
            if (ok && CanEncode(r.Value)) { EncodeRune(r.Value, result); i += used; continue; }
            used = Math.Max(1, used);
            for (int k = 0; k < used; k++) EncodeRune(Kind == CodecKind.SingleByte ? '?' : 0xFFFD, result);
            i += used;
        }
        return result.ToArray();
    }
}

/// <summary>Raised instead of silently replacing characters that the target encoding cannot hold.</summary>
public sealed class UnrepresentableCharactersException(LossReport report)
    : InvalidOperationException($"{report.Total} character(s) cannot be written in the target encoding.")
{
    public LossReport Report { get; } = report;
}

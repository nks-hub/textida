using System.Text;

namespace NksHub.Textida.Core.Encodings;

public enum CodecKind { Utf8, Utf16, Utf32, SingleByte, MultiByte }

/// <summary>How the bytes of a multi-byte encoding are cut into characters: which lead bytes start a longer character and how long it is.</summary>
public enum MultiByteRule { ShiftJis, EucJp, Gbk, Gb18030, Big5, EucKr }

/// <summary>The family a code page is listed under in the encoding picker (by the languages it serves).</summary>
public enum EncodingGroup { Unicode, CentralEuropean, WesternEuropean, Cyrillic, Greek, Turkish, Baltic, Hebrew, Arabic, Asian, Dos }

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
    private readonly Encoding? _multi;                   // multi-byte: the strict .NET encoding of one character (it never replaces anything)
    private readonly MultiByteRule _rule;

    public string Name { get; }
    /// <summary>The group the picker lists this encoding under.</summary>
    public EncodingGroup Group { get; }
    public CodecKind Kind { get; }
    public bool BigEndian { get; }
    /// <summary>Byte order mark written/stripped by this codec variant; empty when it has none.</summary>
    public byte[] Bom { get; }
    /// <summary>Windows code page number, or 0 for tables that do not have one in .NET.</summary>
    public int CodePage { get; }

    /// <summary>True when the target cannot represent every Unicode character (any single-byte page, and every multi-byte encoding but GB18030).</summary>
    public bool IsLossy => Kind == CodecKind.SingleByte || (Kind == CodecKind.MultiByte && _rule != MultiByteRule.Gb18030);

    private TextCodec(string name, CodecKind kind, bool bigEndian, byte[] bom, int codePage, char[]? table, EncodingGroup group = EncodingGroup.Unicode, Encoding? multi = null, MultiByteRule rule = default)
    {
        Name = name; Kind = kind; BigEndian = bigEndian; Bom = bom; CodePage = codePage; Group = group; _multi = multi; _rule = rule;
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
    public static readonly TextCodec Cp1250 = Page("Windows-1250", 1250, EncodingGroup.CentralEuropean);
    public static readonly TextCodec Cp1252 = Page("Windows-1252", 1252, EncodingGroup.WesternEuropean);
    public static readonly TextCodec Latin2 = Page("ISO-8859-2", 28592, EncodingGroup.CentralEuropean);
    private static TextCodec Multi(string name, int codePage, MultiByteRule rule)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return new TextCodec(name, CodecKind.MultiByte, false, [], codePage, null, EncodingGroup.Asian, enc, rule);
    }

    public static readonly TextCodec Kamenicky = new("Kamenický (CP895)", CodecKind.SingleByte, false, [], 0, KamenickyTable.Build(), EncodingGroup.Dos);

    private static TextCodec Page(string name, int codePage, EncodingGroup group) => new(name, CodecKind.SingleByte, false, [], codePage, FromDotNet(codePage), group);

    private static TextCodec Page(string name, EncodingGroup group, char[] table) => new(name, CodecKind.SingleByte, false, [], 0, table, group);

    /// <summary>
    /// The single-byte code pages. Each is a table of 256 characters, so the bytes of a file stay exactly as they are and the large file view reads them as it reads any single-byte page.
    /// ISO-8859-11 is windows-874 below its Thai letters made plain; 8859-10, -14 and -16 come from the mapping tables of the Unicode Consortium because .NET has no data for them.
    /// </summary>
    private static readonly TextCodec[] SingleBytePages =
    [
        Cp1250, Page("Windows-1251", 1251, EncodingGroup.Cyrillic), Cp1252, Page("Windows-1253", 1253, EncodingGroup.Greek), Page("Windows-1254", 1254, EncodingGroup.Turkish),
        Page("Windows-1255", 1255, EncodingGroup.Hebrew), Page("Windows-1256", 1256, EncodingGroup.Arabic), Page("Windows-1257", 1257, EncodingGroup.Baltic), Page("Windows-1258", 1258, EncodingGroup.Asian),
        Page("ISO-8859-1", 28591, EncodingGroup.WesternEuropean), Latin2, Page("ISO-8859-3", 28593, EncodingGroup.WesternEuropean), Page("ISO-8859-4", 28594, EncodingGroup.Baltic),
        Page("ISO-8859-5", 28595, EncodingGroup.Cyrillic), Page("ISO-8859-6", 28596, EncodingGroup.Arabic), Page("ISO-8859-7", 28597, EncodingGroup.Greek), Page("ISO-8859-8", 28598, EncodingGroup.Hebrew),
        Page("ISO-8859-9", 28599, EncodingGroup.Turkish), Page("ISO-8859-10", EncodingGroup.WesternEuropean, IsoTables.Build(IsoTables.Iso10)),
        Page("ISO-8859-11", EncodingGroup.Asian, ThaiIso()), Page("ISO-8859-13", 28603, EncodingGroup.Baltic), Page("ISO-8859-14", EncodingGroup.WesternEuropean, IsoTables.Build(IsoTables.Iso14)),
        Page("ISO-8859-15", 28605, EncodingGroup.WesternEuropean), Page("ISO-8859-16", EncodingGroup.CentralEuropean, IsoTables.Build(IsoTables.Iso16)),
        Page("KOI8-R", 20866, EncodingGroup.Cyrillic), Page("KOI8-U", 21866, EncodingGroup.Cyrillic),
        Page("DOS CP437", 437, EncodingGroup.Dos), Page("DOS CP850", 850, EncodingGroup.Dos), Page("DOS CP852", 852, EncodingGroup.Dos), Page("DOS CP866", 866, EncodingGroup.Dos), Kamenicky,
        Page("Mac Roman", 10000, EncodingGroup.WesternEuropean), Page("Mac Central European", 10029, EncodingGroup.CentralEuropean),
    ];

    /// <summary>
    /// The multi-byte Asian encodings. Their characters are one to four bytes long, so the large file view (which reads blocks of bytes and finds characters by a window of three bytes) does not take them:
    /// <see cref="Documents.PagedBackend.Supports"/> says so. Everywhere else they are decoded and written like the other encodings, with the bytes of an invalid sequence left as they are.
    /// </summary>
    private static readonly TextCodec[] MultiBytePages =
    [
        Multi("Shift-JIS", 932, MultiByteRule.ShiftJis), Multi("EUC-JP", 51932, MultiByteRule.EucJp), Multi("GBK", 936, MultiByteRule.Gbk),
        Multi("GB18030", 54936, MultiByteRule.Gb18030), Multi("Big5", 950, MultiByteRule.Big5), Multi("EUC-KR", 51949, MultiByteRule.EucKr),
    ];

    /// <summary>ISO-8859-11 is TIS-620 with a no-break space at 0xA0; windows-874 has the same Thai letters from 0xA1 and extra characters below them.</summary>
    private static char[] ThaiIso()
    {
        var table = FromDotNet(874);
        for (int b = 0; b <= 0xA0; b++) table[b] = (char)b;
        return table;
    }

    public static IReadOnlyList<TextCodec> All { get; } =
    [
        Utf8, Utf8Bom, Utf16LE, Utf16LEBom, Utf16BE, Utf16BEBom, Utf32LE, Utf32LEBom, Utf32BE, Utf32BEBom,
        .. SingleBytePages, .. MultiBytePages,
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
    public bool SameBytesAs(TextCodec other) => Kind == other.Kind && BigEndian == other.BigEndian && (Kind == CodecKind.SingleByte ? Name == other.Name : CodePage == other.CodePage);

    public override string ToString() => Name;

    // .NET ships the code page data in the shared runtime, so the provider works in trimmed and NativeAOT apps.
    private static char[] FromDotNet(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var table = new char[256];
        var used = new HashSet<char>();
        var one = new byte[1];
        for (int b = 0; b < 256; b++)
        {
            one[0] = (byte)b;
            char c;
            try { c = enc.GetString(one)[0]; }
            catch (DecoderFallbackException) { c = b < 0xA0 ? (char)b : (char)(0xF800 + b); } // undefined slot: a C1 control, or above 0x9F a private-use character, so it round-trips
            if (!used.Add(c)) used.Add(c = (char)(0xF800 + b)); // two bytes for one character: the later one gets its own, so no byte is lost
            table[b] = c;
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
        if (Kind == CodecKind.Utf8 && System.Text.Unicode.Utf8.IsValid(span[bom..])) return DecodeValidUtf8(bytes, bom);
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
            case CodecKind.MultiByte: return MultiCompleteLength(data);
            default: return data.Length;
        }
    }

    // ---- multi-byte units ----

    /// <summary>
    /// The length of the character that starts with <paramref name="d"/>[<paramref name="i"/>] by the lead byte alone (1 for a byte that stands alone). Whether the bytes are a character the encoding has is
    /// decided by the encoding; this only says where a character would end, so a block can be cut between two characters.
    /// </summary>
    private int MultiLength(ReadOnlySpan<byte> d, int i)
    {
        byte b = d[i];
        if (b < 0x80) return 1;
        switch (_rule)
        {
            case MultiByteRule.ShiftJis: return b is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC ? 2 : 1;
            case MultiByteRule.EucJp: return b == 0x8F ? 3 : b == 0x8E || b is >= 0xA1 and <= 0xFE ? 2 : 1;
            case MultiByteRule.Gb18030: return b is >= 0x81 and <= 0xFE ? (i + 1 < d.Length && d[i + 1] is >= 0x30 and <= 0x39 ? 4 : 2) : 1;
            case MultiByteRule.EucKr: return b is >= 0xA1 and <= 0xFE ? 2 : 1;
            default: return b is >= 0x81 and <= 0xFE ? 2 : 1; // GBK and Big5
        }
    }

    /// <summary>Reads the character that starts at <paramref name="i"/>: returns the bytes it takes and its text, or -1 when the bytes are not a character of the encoding (the caller then treats one byte as an invalid unit).</summary>
    private int MultiRead(ReadOnlySpan<byte> d, int i, out string chars)
    {
        int n = MultiLength(d, i);
        chars = "";
        if (n == 1 && d[i] < 0x80) { chars = ((char)d[i]).ToString(); return 1; }
        if (i + n > d.Length) return -1; // cut short: only the end of the data can do that, and it is invalid
        try
        {
            chars = _multi!.GetString(d.Slice(i, n));
            return chars.Length > 0 ? n : -1;
        }
        catch (DecoderFallbackException) { return -1; }
    }

    private int MultiCompleteLength(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            int n = MultiLength(data, i);
            if (i + n > data.Length && n > 1) return i; // starts a character the data does not finish
            i += Math.Min(n, data.Length - i);
        }
        return data.Length;
    }

    private char ReadUnit(ReadOnlySpan<byte> d, int i) =>
        BigEndian ? (char)((d[i] << 8) | d[i + 1]) : (char)((d[i + 1] << 8) | d[i]);

    /// <summary>
    /// Valid UTF-8 in one pass: the system decoder makes the text and the map takes the lead bytes. The unit-by-unit decoder below is for text that is not valid;
    /// both give the same text and map, but this one allocates the text and the map and nothing per character.
    /// </summary>
    private DecodedText DecodeValidUtf8(ReadOnlyMemory<byte> bytes, int bom)
    {
        var body = bytes.Span[bom..];
        string text = Encoding.UTF8.GetString(body);
        var map = new int[text.Length + 1];
        int k = 0;
        for (int i = 0; i < body.Length;)
        {
            byte lead = body[i];
            if (lead < 0x80)
            {
                int run = body[i..].IndexOfAnyExceptInRange((byte)0, (byte)0x7F);
                if (run < 0) run = body.Length - i;
                for (int j = 0; j < run; j++) map[k++] = bom + i + j;
                i += run;
                continue;
            }
            map[k++] = bom + i;
            if (lead >= 0xF0) map[k++] = bom + i; // a supplementary character is two chars that start at one place
            i += lead < 0xE0 ? 2 : lead < 0xF0 ? 3 : 4;
        }
        map[k] = bytes.Length;
        return new DecodedText(this, bytes, bom, text, map, []);
    }

    private void DecodeCore(ReadOnlySpan<byte> d, int baseOffset, StringBuilder text, List<int>? map, List<int>? invalid)
    {
        void AddChar(char c, int at)
        {
            map?.Add(baseOffset + at);
            text.Append(c);
        }
        void AddRune(uint v, int at)
        {
            if (v <= 0xFFFF) AddChar((char)v, at);
            else
            {
                AddChar((char)(0xD7C0 + (v >> 10)), at);
                AddChar((char)(0xDC00 + (v & 0x3FF)), at);
            }
        }
        void AddBad(int at)
        {
            invalid?.Add(text.Length);
            AddChar(Bad[0], at);
        }
        int i = 0;
        switch (Kind)
        {
            case CodecKind.Utf8:
                while (i < d.Length)
                {
                    if (d[i] < 0x80)
                    {
                        int run = d[i..].IndexOfAnyExceptInRange((byte)0, (byte)0x7F);
                        if (run < 0) run = d.Length - i;
                        for (int k = 0; k < run; k++) AddChar((char)d[i + k], i + k);
                        i += run;
                        continue;
                    }
                    var status = Rune.DecodeFromUtf8(d[i..], out Rune r, out int used);
                    bool ok = status == System.Buffers.OperationStatus.Done;
                    if (status == System.Buffers.OperationStatus.NeedMoreData) used = d.Length - i;
                    if (used < 1) used = 1;
                    if (ok) AddRune((uint)r.Value, i);
                    else AddBad(i);
                    i += used;
                }
                break;
            case CodecKind.Utf16:
                while (i < d.Length)
                {
                    if (i + 1 >= d.Length) { AddBad(i); break; }
                    char u = ReadUnit(d, i);
                    if (char.IsHighSurrogate(u) && i + 3 < d.Length && char.IsLowSurrogate(ReadUnit(d, i + 2)))
                    {
                        AddChar(u, i);
                        AddChar(ReadUnit(d, i + 2), i);
                        i += 4;
                    }
                    else if (char.IsSurrogate(u)) { AddBad(i); i += 2; }
                    else { AddChar(u, i); i += 2; }
                }
                break;
            case CodecKind.Utf32:
                while (i < d.Length)
                {
                    if (i + 3 >= d.Length) { AddBad(i); break; }
                    uint v = BigEndian
                        ? (uint)(d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3])
                        : (uint)(d[i + 3] << 24 | d[i + 2] << 16 | d[i + 1] << 8 | d[i]);
                    if (Rune.IsValid(v)) AddRune(v, i);
                    else AddBad(i);
                    i += 4;
                }
                break;
            case CodecKind.MultiByte:
                while (i < d.Length)
                {
                    int used = MultiRead(d, i, out var decoded);
                    if (used < 0) { AddBad(i); i++; continue; }
                    foreach (char c in decoded) AddChar(c, i);
                    i += used;
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
        if (Kind == CodecKind.Utf8)
        {
            if (Ascii.IsValid(bytes)) return Encoding.ASCII.GetString(bytes);
            // text that is valid UTF-8 decodes the same everywhere; the unit-by-unit path below costs a string per character, which a selection of megabytes cannot afford
            if (System.Text.Unicode.Utf8.IsValid(bytes)) return Encoding.UTF8.GetString(bytes);
        }
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
            case CodecKind.MultiByte:
                int taken = MultiRead(data, 0, out var unit);
                chars = taken < 0 ? 1 : unit.Length;
                return taken < 0 ? 1 : taken;
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
            case CodecKind.MultiByte:
                // not self-synchronising: walk from the last byte that can only start a character (below 0x40, or below 0x80 for the EUC encodings), or from the start of the window when it holds none
                int syncBelow = _rule is MultiByteRule.EucJp or MultiByteRule.EucKr ? 0x80 : 0x40, start = pos - 1;
                while (start > 0 && window[start] >= syncBelow) start--;
                int cursor = start;
                while (cursor < pos) cursor += MultiRead(window, cursor, out _) is var step and > 0 ? step : 1;
                return cursor == pos;
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
        if (Kind == CodecKind.MultiByte)
        {
            try { _multi!.GetBytes(char.ConvertFromUtf32(codePoint)); return true; }
            catch (EncoderFallbackException) { return false; }
        }
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
            case CodecKind.MultiByte:
                foreach (byte b in _multi!.GetBytes(char.ConvertFromUtf32(codePoint))) output.Add(b);
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
            case CodecKind.MultiByte: return EncodeLossy(text).Length;
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
            for (int k = 0; k < used; k++) EncodeRune(Kind is CodecKind.SingleByte or CodecKind.MultiByte ? '?' : 0xFFFD, result);
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

using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Hex;

/// <summary>How a byte looks in the character column of the hex view.</summary>
public enum HexCellKind
{
    Normal,
    /// <summary>A control or undefined byte, drawn as a dim dot.</summary>
    Control,
    /// <summary>The second and later bytes of a character, drawn as a thin dot.</summary>
    Continuation,
    /// <summary>A byte that is no valid character in the encoding, drawn as a highlighted replacement character.</summary>
    Invalid,
}

public readonly record struct HexCell(string Glyph, HexCellKind Kind);

/// <summary>Encoding of the character column; independent of the encoding of the document.</summary>
public sealed class HexCharset
{
    private readonly HexCell[]? _table;

    private HexCharset(string name, TextCodec codec)
    {
        Name = name;
        Codec = codec;
        if (codec.Kind != CodecKind.SingleByte) return;
        _table = new HexCell[256];
        for (int b = 0; b < 256; b++)
        {
            string s = codec.GetString([(byte)b]);
            bool control = b < 0x20 || b == 0x7F || s.Length != 1 || s[0] == '�' || char.IsControl(s[0]) || char.IsWhiteSpace(s[0]) && s[0] != ' ';
            _table[b] = control ? new("·", HexCellKind.Control) : new(s, HexCellKind.Normal);
        }
    }

    public string Name { get; }
    public TextCodec Codec { get; }

    public static readonly HexCharset Windows1250 = new("Windows-1250", TextCodec.Cp1250);
    public static readonly HexCharset Windows1252 = new("Windows-1252", TextCodec.Cp1252);
    public static readonly HexCharset Latin2 = new("ISO-8859-2", TextCodec.Latin2);
    public static readonly HexCharset Utf8 = new("UTF-8", TextCodec.Utf8);
    public static readonly HexCharset Utf16LE = new("UTF-16 LE", TextCodec.Utf16LE);
    public static readonly HexCharset Utf16BE = new("UTF-16 BE", TextCodec.Utf16BE);

    public static IReadOnlyList<HexCharset> All { get; } = [Windows1250, Windows1252, Latin2, Utf8, Utf16LE, Utf16BE];

    /// <summary>The charset that shows a document encoding best (the single-byte code page, or the Unicode form itself).</summary>
    public static HexCharset For(TextCodec codec) =>
        All.FirstOrDefault(c => c.Codec.SameBytesAs(codec)) ?? (codec.Kind == CodecKind.SingleByte ? Windows1252 : Utf8);

    /// <summary>Bytes in front of a row that the mapping needs to classify the bytes of the row.</summary>
    public int Lookbehind => Codec.Kind switch { CodecKind.Utf8 => 3, CodecKind.Utf16 => 2, _ => 0 };

    /// <summary>Bytes after a row that the mapping needs to finish a character that starts in the row.</summary>
    public int Lookahead => Codec.Kind switch { CodecKind.Utf8 => 3, CodecKind.Utf16 => 2, _ => 0 };

    /// <summary>
    /// Cells of the <paramref name="count"/> bytes that start at <paramref name="first"/> of <paramref name="window"/>. The window holds up to
    /// <see cref="Lookbehind"/> bytes before and <see cref="Lookahead"/> after them, and starts at the file offset <paramref name="windowOffset"/>.
    /// </summary>
    public void Map(ReadOnlySpan<byte> window, int first, int count, long windowOffset, Span<HexCell> cells)
    {
        switch (Codec.Kind)
        {
            case CodecKind.SingleByte:
                for (int i = 0; i < count; i++) cells[i] = _table![window[first + i]];
                break;
            case CodecKind.Utf8:
                MapUtf8(window, first, count, cells);
                break;
            default:
                MapUtf16(window, first, count, windowOffset, cells);
                break;
        }
    }

    private static void MapUtf8(ReadOnlySpan<byte> window, int first, int count, Span<HexCell> cells)
    {
        int end = first + count;
        // a unit that starts before the row is decoded too, so its continuation bytes inside the row are not taken for invalid ones
        int at = Math.Max(0, first - 3);
        while (at < end)
        {
            byte lead = window[at];
            int length;
            HexCell head;
            if (lead < 0x80)
            {
                length = 1;
                head = lead < 0x20 || lead == 0x7F ? new("·", HexCellKind.Control) : new(((char)lead).ToString(), HexCellKind.Normal);
            }
            else if (Rune.DecodeFromUtf8(window[at..], out var rune, out int used) == System.Buffers.OperationStatus.Done)
            {
                length = used;
                head = Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.OtherNotAssigned
                    or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator
                    ? new("·", HexCellKind.Control) : new(rune.ToString(), HexCellKind.Normal);
            }
            else
            {
                length = 1;
                head = new("�", HexCellKind.Invalid);
            }
            for (int k = 0; k < length && at + k < end; k++)
                if (at + k >= first) cells[at + k - first] = k == 0 ? head : new("·", HexCellKind.Continuation);
            at += length;
        }
    }

    private void MapUtf16(ReadOnlySpan<byte> window, int first, int count, long windowOffset, Span<HexCell> cells)
    {
        bool big = Codec.BigEndian;
        int end = first + count;
        int at = first;
        // a pair that starts before the row has its head in the window
        if (at >= 2 && Unit(window, at - 2, big) is { } prev && char.IsHighSurrogate(prev) && Unit(window, at, big) is { } cur && char.IsLowSurrogate(cur)) at -= 2;
        while (at < end)
        {
            int length;
            HexCell head;
            if (at + 1 >= window.Length)
            {
                length = 1;
                head = new("·", HexCellKind.Control);
            }
            else
            {
                char u = Unit(window, at, big)!.Value;
                if (char.IsHighSurrogate(u) && at + 3 < window.Length && Unit(window, at + 2, big) is { } low && char.IsLowSurrogate(low))
                {
                    length = 4;
                    head = new(new string([u, low]), HexCellKind.Normal);
                }
                else if (char.IsSurrogate(u))
                {
                    length = 2;
                    head = new("�", HexCellKind.Invalid);
                }
                else
                {
                    length = 2;
                    head = char.IsControl(u) || char.IsWhiteSpace(u) && u != ' ' || char.GetUnicodeCategory(u) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.OtherNotAssigned
                        ? new("·", HexCellKind.Control) : new(u.ToString(), HexCellKind.Normal);
                }
            }
            for (int k = 0; k < length && at + k < end; k++)
                if (at + k >= first) cells[at + k - first] = k == 0 ? head : new("·", HexCellKind.Continuation);
            at += length;
        }
    }

    private static char? Unit(ReadOnlySpan<byte> w, int at, bool big) =>
        at + 1 < w.Length ? (char)(big ? w[at] << 8 | w[at + 1] : w[at + 1] << 8 | w[at]) : null;
}

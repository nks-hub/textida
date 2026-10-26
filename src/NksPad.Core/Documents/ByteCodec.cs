using System.Buffers.Binary;
using System.Text;

namespace NksPad.Core.Documents;

/// <summary>
/// Character boundaries and decoding of a document's bytes. A unit is the smallest run of bytes that decodes to
/// one or more UTF-16 chars; an invalid sequence is one unit that decodes to one U+FFFD. Supports UTF-8, UTF-16 and
/// single-byte code pages; the same rules serve every backend, so they agree on positions.
/// </summary>
public sealed class ByteCodec
{
    private enum Mode { Utf8, Utf16LE, Utf16BE, SingleByte }

    private readonly Mode _mode;

    static ByteCodec() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public ByteCodec(DetectedEncoding format)
    {
        Format = format;
        switch (format.Kind)
        {
            case EncodingKind.Ascii or EncodingKind.Utf8:
                _mode = Mode.Utf8;
                Encoding = new UTF8Encoding(false);
                break;
            case EncodingKind.Utf16LE:
                _mode = Mode.Utf16LE;
                Encoding = new UnicodeEncoding(false, false);
                break;
            case EncodingKind.Utf16BE:
                _mode = Mode.Utf16BE;
                Encoding = new UnicodeEncoding(true, false);
                break;
            case EncodingKind.SingleByte when format.Encoding.IsSingleByte:
                _mode = Mode.SingleByte;
                Encoding = Encoding.GetEncoding(format.Encoding.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                break;
            default:
                throw new NotSupportedException($"{format.Kind} is not supported by the document model.");
        }
    }

    public DetectedEncoding Format { get; }
    public int BomLength => Format.BomLength;
    /// <summary>Encoding without preamble and with replacement fallbacks in both directions.</summary>
    public Encoding Encoding { get; }
    public bool IsUtf16 => _mode is Mode.Utf16LE or Mode.Utf16BE;
    /// <summary>Windows handed to the boundary methods must start at a multiple of this (relative to the end of the BOM).</summary>
    public int Alignment => IsUtf16 ? 2 : 1;

    public string Decode(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);
    public int CountChars(ReadOnlySpan<byte> bytes) => Encoding.GetCharCount(bytes);
    public byte[] Encode(ReadOnlySpan<char> text)
    {
        var bytes = new byte[Encoding.GetByteCount(text)];
        Encoding.GetBytes(text, bytes);
        return bytes;
    }

    /// <summary>Reads the unit at the start of <paramref name="data"/> (which begins on a boundary) and returns its byte length.</summary>
    public int ReadUnit(ReadOnlySpan<byte> data, out int chars)
    {
        chars = 1;
        if (data.IsEmpty) return 0;
        switch (_mode)
        {
            case Mode.Utf8:
                if (data[0] < 0x80) return 1;
                System.Text.Rune.DecodeFromUtf8(data, out var rune, out int used);
                chars = rune.Utf16SequenceLength;
                return Math.Max(1, used);
            case Mode.SingleByte:
                return 1;
            default:
                if (data.Length < 2) return 1;
                if (char.IsHighSurrogate((char)ReadU16(data, 0)) && data.Length >= 4 && char.IsLowSurrogate((char)ReadU16(data, 2)))
                {
                    chars = 2;
                    return 4;
                }
                return 2;
        }
    }

    private ushort ReadU16(ReadOnlySpan<byte> d, int at) =>
        _mode == Mode.Utf16LE ? BinaryPrimitives.ReadUInt16LittleEndian(d[at..]) : BinaryPrimitives.ReadUInt16BigEndian(d[at..]);

    /// <summary>
    /// True when <paramref name="pos"/> starts a unit. <paramref name="window"/> must hold the bytes around it:
    /// at least 3 before (or up to the first editable byte) and 4 after (or up to the end of the document).
    /// </summary>
    public bool IsBoundary(ReadOnlySpan<byte> window, int pos)
    {
        if (pos <= 0 || pos >= window.Length) return true;
        switch (_mode)
        {
            case Mode.SingleByte:
                return true;
            case Mode.Utf8:
                if ((window[pos] & 0xC0) != 0x80) return true;
                int lead = pos - 1, floor = Math.Max(0, pos - 3);
                while (lead >= floor && (window[lead] & 0xC0) == 0x80) lead--;
                if (lead < floor) return true;
                int i = lead;
                while (i < pos) i += ReadUnit(window[i..], out _);
                return i == pos;
            default:
                if ((pos & 1) != 0) return false;
                return !(pos >= 2 && pos + 2 <= window.Length && char.IsHighSurrogate((char)ReadU16(window, pos - 2)) && char.IsLowSurrogate((char)ReadU16(window, pos)));
        }
    }

    /// <summary>True when <paramref name="pos"/> lies between the CR and the LF of one CRLF terminator.</summary>
    public bool IsInsideCrLf(ReadOnlySpan<byte> window, int pos)
    {
        switch (_mode)
        {
            case Mode.Utf16LE:
                return pos >= 2 && pos + 2 <= window.Length && window[pos - 2] == 13 && window[pos - 1] == 0 && window[pos] == 10 && window[pos + 1] == 0;
            case Mode.Utf16BE:
                return pos >= 2 && pos + 2 <= window.Length && window[pos - 2] == 0 && window[pos - 1] == 13 && window[pos] == 0 && window[pos + 1] == 10;
            default:
                return pos >= 1 && pos < window.Length && window[pos - 1] == 13 && window[pos] == 10;
        }
    }
}

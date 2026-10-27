using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>The text forms "Copy as" and "Export as" write bytes in.</summary>
public enum ByteFormat { HexSpaced, HexPlain, CArray, CSharp, Java, Python, Rust, Go, JavaScript, Pascal, Base64, IntelHex, SRecord, HexDump }

/// <summary>What "Paste as" reads text as.</summary>
public enum ByteTextKind { Hex, Base64, CArray }

/// <summary>
/// Bytes as text in the forms of <see cref="ByteFormat"/>, written block by block so a selection of any size can be exported, and the way back for the
/// forms a paste understands. Addresses of Intel HEX, S-record and hexdump are the offsets of the bytes in the document.
/// </summary>
public static class ByteFormats
{
    private const int Block = 48 * 1024;
    private const int PerLine = 16;

    public static IReadOnlyList<ByteFormat> All { get; } = Enum.GetValues<ByteFormat>();

    /// <summary>Usual file extension of the form, with the dot.</summary>
    public static string Extension(ByteFormat format) => format switch
    {
        ByteFormat.CArray => ".h",
        ByteFormat.CSharp => ".cs",
        ByteFormat.Java => ".java",
        ByteFormat.Python => ".py",
        ByteFormat.Rust => ".rs",
        ByteFormat.Go => ".go",
        ByteFormat.JavaScript => ".js",
        ByteFormat.Pascal => ".pas",
        ByteFormat.Base64 => ".b64",
        ByteFormat.IntelHex => ".hex",
        ByteFormat.SRecord => ".srec",
        _ => ".txt",
    };

    /// <summary>The bytes of <paramref name="bytes"/> as text; addresses start at <paramref name="address"/>.</summary>
    public static string Format(byte[] bytes, ByteFormat format, long address = 0)
    {
        var text = new StringWriter(CultureInfo.InvariantCulture);
        Write(text, format, new ArraySnapshot(bytes), new ByteRange(0, bytes.Length), address);
        return text.ToString();
    }

    /// <summary>
    /// Writes <paramref name="range"/> of <paramref name="source"/> in the form. <paramref name="address"/> is the address of its first byte for the forms that
    /// have addresses; Intel HEX and S-record hold 32-bit addresses and refuse more with <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static void Write(TextWriter output, ByteFormat format, IByteSnapshot source, ByteRange range, long address, CancellationToken cancellationToken = default)
    {
        if (range.End > source.ByteLength) throw new ArgumentOutOfRangeException(nameof(range));
        if (format is ByteFormat.IntelHex or ByteFormat.SRecord && address + range.Length > uint.MaxValue + 1L)
            throw new ArgumentOutOfRangeException(nameof(address), "The form holds 32-bit addresses.");
        long length = range.Length;
        var style = format switch
        {
            ByteFormat.CArray => new ArrayStyle("unsigned char data[] = {", "0x", "\n};"),
            ByteFormat.CSharp => new ArrayStyle("byte[] data = new byte[] {", "0x", "\n};"),
            ByteFormat.Java => new ArrayStyle("byte[] data = {", "(byte) 0x", "\n};"),
            ByteFormat.Python => new ArrayStyle("data = bytes([", "0x", "\n])"),
            ByteFormat.Rust => new ArrayStyle($"const DATA: [u8; {length}] = [", "0x", "\n];"),
            ByteFormat.Go => new ArrayStyle("var data = []byte{", "0x", "\n}"),
            ByteFormat.JavaScript => new ArrayStyle("const data = new Uint8Array([", "0x", "\n]);"),
            ByteFormat.Pascal => new ArrayStyle($"const\n  Data: array[0..{length - 1}] of Byte = (", "$", "\n  );"),
            _ => null,
        };
        if (style is not null) output.Write(style.Head);
        var writer = new Writer(output, format, style, address, length);
        writer.Start();
        var buffer = new byte[Block];
        for (long at = range.Start; at < range.End;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = source.Read(at, buffer.AsSpan(0, (int)Math.Min(Block, range.End - at)));
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            writer.Append(buffer.AsSpan(0, n));
            at += n;
        }
        writer.Finish();
    }

    private sealed record ArrayStyle(string Head, string Prefix, string Tail);

    private sealed class Writer(TextWriter output, ByteFormat format, ArrayStyle? style, long address, long length)
    {
        private long _done;
        private long _upper;
        private readonly StringBuilder _line = new();
        // S-record: the width of the addresses follows from the last byte, so it is known before the first data record
        private readonly int _addressBytes = AddressWidth(address + Math.Max(0, length - 1));

        private static int AddressWidth(long last) => last <= 0xFFFF ? 2 : last <= 0xFFFFFF ? 3 : 4;

        public void Start()
        {
            if (format == ByteFormat.SRecord) output.Write(SRecord(0, 0, "data"u8));
        }

        public void Append(ReadOnlySpan<byte> block)
        {
            switch (format)
            {
                case ByteFormat.HexSpaced or ByteFormat.HexPlain:
                    foreach (byte b in block)
                    {
                        if (format == ByteFormat.HexSpaced && _done > 0) output.Write(' ');
                        output.Write(b.ToString("X2", CultureInfo.InvariantCulture));
                        _done++;
                    }
                    break;
                case ByteFormat.Base64:
                    // blocks are multiples of 3 bytes, so no padding appears before the end
                    output.Write(Convert.ToBase64String(block));
                    _done += block.Length;
                    break;
                case ByteFormat.IntelHex or ByteFormat.SRecord or ByteFormat.HexDump:
                    for (int i = 0; i < block.Length; i += PerLine)
                    {
                        var line = block[i..Math.Min(block.Length, i + PerLine)];
                        if (format == ByteFormat.IntelHex) IntelLine(line);
                        else if (format == ByteFormat.SRecord) SLine(line);
                        else DumpLine(line);
                        _done += line.Length;
                    }
                    break;
                default:
                    foreach (byte b in block)
                    {
                        if (_done > 0) output.Write(',');
                        output.Write(_done % PerLine == 0 ? "\n    " : " ");
                        output.Write(style!.Prefix);
                        output.Write(b.ToString("X2", CultureInfo.InvariantCulture));
                        _done++;
                    }
                    break;
            }
        }

        public void Finish()
        {
            switch (format)
            {
                case ByteFormat.IntelHex:
                    output.Write(":00000001FF\n");
                    break;
                case ByteFormat.SRecord:
                    output.Write(SRecord(11 - _addressBytes, 0, []));
                    break;
                case ByteFormat.HexDump:
                    output.Write(HexOffset(address + _done) + "\n");
                    break;
                default:
                    if (style is not null) output.Write(style.Tail);
                    break;
            }
        }

        // ---- Intel HEX ----

        // a record never crosses a 64 KiB boundary; the upper 16 bits of the address go in an extended linear address record when they change
        private void IntelLine(ReadOnlySpan<byte> line)
        {
            long at = address + _done;
            while (line.Length > 0)
            {
                int n = (int)Math.Min(line.Length, 0x10000 - (at & 0xFFFF));
                if (at >> 16 != _upper)
                {
                    _upper = at >> 16;
                    IntelRecord(0, 4, [(byte)(_upper >> 8), (byte)_upper]);
                }
                IntelRecord((int)(at & 0xFFFF), 0, line[..n]);
                line = line[n..];
                at += n;
            }
        }

        private void IntelRecord(int offset, int type, ReadOnlySpan<byte> data)
        {
            int sum = data.Length + (offset >> 8) + (offset & 0xFF) + type;
            output.Write($":{data.Length:X2}{offset:X4}{type:X2}");
            foreach (byte b in data)
            {
                output.Write(b.ToString("X2", CultureInfo.InvariantCulture));
                sum += b;
            }
            output.Write(((-sum) & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
            output.Write('\n');
        }

        // ---- Motorola S-record ----

        private void SLine(ReadOnlySpan<byte> line) => output.Write(SRecord(_addressBytes - 1, address + _done, line));

        private static string SRecord(int type, long at, ReadOnlySpan<byte> data)
        {
            int addressBytes = type switch { 0 or 1 or 9 => 2, 2 or 8 => 3, _ => 4 };
            var record = new StringBuilder();
            int count = addressBytes + data.Length + 1;
            int sum = count;
            record.Append('S').Append(type).Append(count.ToString("X2", CultureInfo.InvariantCulture));
            for (int shift = (addressBytes - 1) * 8; shift >= 0; shift -= 8)
            {
                int b = (int)(at >> shift & 0xFF);
                record.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                sum += b;
            }
            foreach (byte b in data)
            {
                record.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                sum += b;
            }
            record.Append((~sum & 0xFF).ToString("X2", CultureInfo.InvariantCulture)).Append('\n');
            return record.ToString();
        }

        // ---- hexdump ----

        private string HexOffset(long at) => at.ToString("x8", CultureInfo.InvariantCulture);

        private void DumpLine(ReadOnlySpan<byte> line)
        {
            _line.Clear();
            _line.Append(HexOffset(address + _done)).Append("  ");
            for (int i = 0; i < PerLine; i++)
            {
                if (i == 8) _line.Append(' ');
                if (i < line.Length) _line.Append(line[i].ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
                else _line.Append("   ");
            }
            _line.Append(" |");
            foreach (byte b in line) _line.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            _line.Append("|\n");
            output.Write(_line.ToString());
        }
    }

    // ---- reading ----

    /// <summary>Reads text as bytes: hex digits, Base64 or the numbers of a source-code array. False, with the reason, when it is none of that.</summary>
    public static bool TryParse(ByteTextKind kind, string text, out byte[] bytes, out string problem)
    {
        bytes = [];
        problem = "";
        switch (kind)
        {
            case ByteTextKind.Hex:
                if (HexEditor.TryParseHexText(text, out bytes)) return true;
                problem = "The text is not hex digits in pairs.";
                return false;
            case ByteTextKind.Base64:
                var compact = new string([.. text.Where(c => !char.IsWhiteSpace(c))]);
                var buffer = new byte[compact.Length * 3 / 4 + 3];
                if (compact.Length > 0 && Convert.TryFromBase64String(compact, buffer, out int written)) { bytes = buffer[..written]; return true; }
                problem = "The text is not Base64.";
                return false;
            default:
                return TryParseArray(text, out bytes, out problem);
        }
    }

    private static readonly Regex Comments = new(@"//[^\n]*|/\*.*?\*/|#[^\n]*", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"(?<![\w$])-?(0[xX][0-9A-Fa-f]+|\$[0-9A-Fa-f]+|0[bB][01]+|\d+)(?![\w$])", RegexOptions.CultureInvariant);

    private static bool TryParseArray(string text, out byte[] bytes, out string problem)
    {
        bytes = [];
        problem = "";
        string body = Comments.Replace(text, " ");
        int open = body.IndexOf('{'), close = body.LastIndexOf('}');
        if (open >= 0 && close > open) body = body[(open + 1)..close];
        else if (body.IndexOf('=') is var equals and >= 0) body = body[(equals + 1)..];
        var values = new List<byte>();
        foreach (Match m in Number.Matches(body))
        {
            string t = m.Value;
            bool negative = t.StartsWith('-');
            if (negative) t = t[1..];
            long v;
            try
            {
                v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(t[2..], 16)
                    : t.StartsWith('$') ? Convert.ToInt64(t[1..], 16)
                    : t.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(t[2..], 2)
                    : long.Parse(t, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
            {
                problem = $"'{m.Value}' is not a byte value.";
                return false;
            }
            if (negative) v = -v;
            if (v is < -128 or > 255)
            {
                problem = $"'{m.Value}' is not a byte value.";
                return false;
            }
            values.Add((byte)v);
        }
        if (values.Count == 0)
        {
            problem = "The text holds no numbers.";
            return false;
        }
        bytes = [.. values];
        return true;
    }
}

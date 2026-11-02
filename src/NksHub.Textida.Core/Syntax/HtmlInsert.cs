using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;

namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// The pieces of "Insert Image" and "Insert Link": the pixel size of an image file, the address of a file as a relative reference from the document, and the tags.
/// shortcut: the size is read for PNG, GIF, JPEG, BMP, WebP, SVG, ICO, AVIF and HEIF; any other file gets an image tag without a size. Of several pictures in one ICO, AVIF or HEIF file the largest counts, and a rotation flag is not applied.
/// </summary>
public static class HtmlInsert
{
    public static readonly IReadOnlyList<string> ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.svg", "*.ico", "*.avif", "*.heic", "*.heif"];

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".avif", ".heic", ".heif"];

    /// <summary>JPEG segments looked through for the frame header before the file counts as having none; a real file has it among the first few.</summary>
    private const int MaxJpegSegments = 1024;

    /// <summary>How much of the start of an SVG, ICO, AVIF or HEIF file is looked through for the size.</summary>
    private const int MaxHeadBytes = 64 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the width and height in pixels from the header of a PNG, GIF, JPEG, BMP, WebP, SVG, ICO, AVIF or HEIF image; false for anything else or a damaged header.</summary>
    public static bool TryImageSize(Stream image, out int width, out int height)
    {
        width = height = 0;
        Span<byte> head = stackalloc byte[32];
        int got = ReadUpTo(image, head);
        var h = head[..got];
        if (h.Length >= 24 && h[..8].SequenceEqual(PngSignature) && h[12..16].SequenceEqual("IHDR"u8))
            (width, height) = (BinaryPrimitives.ReadInt32BigEndian(h[16..]), BinaryPrimitives.ReadInt32BigEndian(h[20..]));
        else if (h.Length >= 10 && (h[..6].SequenceEqual("GIF87a"u8) || h[..6].SequenceEqual("GIF89a"u8)))
            (width, height) = (BinaryPrimitives.ReadUInt16LittleEndian(h[6..]), BinaryPrimitives.ReadUInt16LittleEndian(h[8..]));
        else if (h.Length >= 26 && h[..2].SequenceEqual("BM"u8))
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(h[14..]);
            if (header == 12) (width, height) = (BinaryPrimitives.ReadUInt16LittleEndian(h[18..]), BinaryPrimitives.ReadUInt16LittleEndian(h[20..]));
            else if (header >= 40) (width, height) = (BinaryPrimitives.ReadInt32LittleEndian(h[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h[22..]))); // a negative height means rows from the top
        }
        else if (h.Length >= 30 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
            (width, height) = WebPSize(h);
        else if (h.Length >= 4 && h[0] == 0xFF && h[1] == 0xD8)
            (width, height) = JpegSize(image);
        else if (image.CanSeek && ReadHead(image) is { Length: > 0 } big)
            (width, height) = IconSize(big) is { Width: > 0 } icon ? icon : IsoSize(big) is { Width: > 0 } iso ? iso : SvgSize(big);
        return width > 0 && height > 0;
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static byte[] ReadHead(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[MaxHeadBytes];
        return buffer[..ReadUpTo(stream, buffer)];
    }

    /// <summary>The largest picture listed in the directory of an ICO file (an entry of 0 means 256).</summary>
    private static (int Width, int Height) IconSize(byte[] d)
    {
        if (d.Length < 22 || d[0] != 0 || d[1] != 0 || d[2] != 1 || d[3] != 0) return (0, 0);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(4));
        if (count == 0 || d[9] != 0) return (0, 0); // the reserved byte of the first entry
        (int Width, int Height) best = (0, 0);
        for (int i = 0; i < count && 6 + (i + 1) * 16 <= d.Length; i++)
        {
            int w = d[6 + i * 16] is 0 ? 256 : d[6 + i * 16], h = d[7 + i * 16] is 0 ? 256 : d[7 + i * 16];
            if (w * h > best.Width * best.Height) best = (w, h);
        }
        return best;
    }

    private static readonly string[] IsoBrands = ["avif", "avis", "heic", "heix", "heim", "heis", "hevc", "mif1", "msf1"];

    /// <summary>The largest <c>ispe</c> (image spatial extent) property of an AVIF or HEIF file, found through <c>meta</c>, <c>iprp</c> and <c>ipco</c>.</summary>
    private static (int Width, int Height) IsoSize(byte[] d)
    {
        if (d.Length < 16 || !d.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return (0, 0);
        int ftypSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(d), (uint)d.Length);
        bool known = false;
        for (int at = 8; at + 4 <= ftypSize && !known; at += 4)
            if (at != 12) known = IsoBrands.Contains(Encoding.ASCII.GetString(d, at, 4)); // bytes 12..15 are the minor version
        if (!known) return (0, 0);
        (int Width, int Height) best = (0, 0);
        FindIspe(d, ref best, 0);
        return best;
    }

    private static void FindIspe(ReadOnlySpan<byte> data, ref (int Width, int Height) best, int depth)
    {
        while (data.Length >= 8)
        {
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(data);
            int header = 8;
            if (size == 1)
            {
                if (data.Length < 16) return;
                size = BinaryPrimitives.ReadUInt64BigEndian(data[8..]);
                header = 16;
            }
            else if (size == 0) size = (ulong)data.Length;
            if (size < (ulong)header) return;
            int total = (int)Math.Min(size, (ulong)data.Length); // a box cut off by the end of what was read still counts as far as it goes
            var body = data[header..total];
            var type = data[4..8];
            if (type.SequenceEqual("ispe"u8) && body.Length >= 12)
            {
                uint w = BinaryPrimitives.ReadUInt32BigEndian(body[4..]), h = BinaryPrimitives.ReadUInt32BigEndian(body[8..]);
                if (w is > 0 and <= int.MaxValue && h is > 0 and <= int.MaxValue && (long)w * h > (long)best.Width * best.Height) best = ((int)w, (int)h);
            }
            else if (depth < 4 && type.SequenceEqual("meta"u8) && body.Length >= 4) FindIspe(body[4..], ref best, depth + 1); // meta is a full box: version and flags first
            else if (depth < 4 && (type.SequenceEqual("iprp"u8) || type.SequenceEqual("ipco"u8))) FindIspe(body, ref best, depth + 1);
            data = data[total..];
        }
    }

    /// <summary>The <c>width</c> and <c>height</c> of the root <c>svg</c> element, or its <c>viewBox</c> when either is missing or relative. No DTD is processed and nothing is fetched.</summary>
    private static (int Width, int Height) SvgSize(byte[] d)
    {
        int start = d.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0;
        while (start < d.Length && d[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        if (start >= d.Length || d[start] != (byte)'<' && d[start] != 0xFE && d[start] != 0xFF) return (0, 0);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1 << 20 };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(d), settings);
            while (reader.Read() && reader.NodeType != XmlNodeType.Element) { }
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "svg") return (0, 0);
            if (SvgLength(reader.GetAttribute("width")) is { } w && SvgLength(reader.GetAttribute("height")) is { } h) return (w, h);
            var box = reader.GetAttribute("viewBox")?.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            return box is { Length: 4 }
                && double.TryParse(box[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double bw)
                && double.TryParse(box[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double bh)
                && ToPixels(bw) is { } vw && ToPixels(bh) is { } vh ? (vw, vh) : (0, 0);
        }
        catch (XmlException) { return (0, 0); }
    }

    private static int? ToPixels(double value) => value > 0 && value < 1_000_000 ? Math.Max(1, (int)Math.Round(value)) : null;

    /// <summary>An SVG length as pixels: a plain number or one with px, pt, pc, in, cm or mm; null for a percentage, em, ex or anything else.</summary>
    private static int? SvgLength(string? text)
    {
        if (text is null) return null;
        text = text.Trim();
        int unit = text.Length;
        while (unit > 0 && char.IsAsciiLetter(text[unit - 1])) unit--;
        double factor = text[unit..].ToLowerInvariant() switch
        {
            "" or "px" => 1, "pt" => 96 / 72.0, "pc" => 16, "in" => 96, "cm" => 96 / 2.54, "mm" => 96 / 25.4, _ => 0,
        };
        return factor > 0 && double.TryParse(text[..unit], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? ToPixels(n * factor) : null;
    }

    private static (int Width, int Height) WebPSize(ReadOnlySpan<byte> h)
    {
        var kind = h[12..16];
        if (kind.SequenceEqual("VP8 "u8))
            return h[23] == 0x9D && h[24] == 0x01 && h[25] == 0x2A ? (BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF) : (0, 0);
        if (kind.SequenceEqual("VP8L"u8))
        {
            if (h[20] != 0x2F) return (0, 0);
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(h[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        if (kind.SequenceEqual("VP8X"u8))
            return ((h[24] | h[25] << 8 | h[26] << 16) + 1, (h[27] | h[28] << 8 | h[29] << 16) + 1);
        return (0, 0);
    }

    /// <summary>Walks the segments of a JPEG (after its first two bytes) up to the frame header, which holds the size. The stream has to be seekable.</summary>
    private static (int Width, int Height) JpegSize(Stream stream)
    {
        if (!stream.CanSeek) return (0, 0);
        stream.Position = 2;
        Span<byte> segment = stackalloc byte[7];
        for (int count = 0; count < MaxJpegSegments; count++)
        {
            if (stream.ReadByte() != 0xFF) return (0, 0);
            int marker;
            do marker = stream.ReadByte(); while (marker == 0xFF); // fill bytes before a marker
            if (marker < 0) return (0, 0);
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue; // markers without a length
            if (marker is 0xD8 or 0xD9 or 0xDA) return (0, 0); // a second image start, the end, the start of the scan: no frame header came before
            if (ReadUpTo(stream, segment[..2]) < 2) return (0, 0);
            int length = BinaryPrimitives.ReadUInt16BigEndian(segment);
            if (length < 2) return (0, 0);
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7 || ReadUpTo(stream, segment[..5]) < 5) return (0, 0);
                return (BinaryPrimitives.ReadUInt16BigEndian(segment[3..]), BinaryPrimitives.ReadUInt16BigEndian(segment[1..]));
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return (0, 0);
    }

    /// <summary>
    /// The address of <paramref name="file"/> as a reference relative to <paramref name="documentDirectory"/>: forward slashes, and every character that is not a letter,
    /// digit, <c>-._~</c> or beyond ASCII written as <c>%XX</c>. A file on another drive or with no folder to start from becomes a <c>file:///</c> address.
    /// </summary>
    public static string RelativeReference(string? documentDirectory, string file)
    {
        string full = Path.GetFullPath(file);
        string relative = documentDirectory is null ? full : Path.GetRelativePath(documentDirectory, full);
        if (Path.IsPathRooted(relative)) return full.StartsWith(@"\\", StringComparison.Ordinal) ? "file:" + EscapePath(full.Replace('\\', '/')) : "file:///" + EscapePath(full.Replace('\\', '/').TrimStart('/'));
        string reference = EscapePath(relative.Replace('\\', '/'));
        int slash = reference.IndexOf('/');
        return reference.AsSpan(0, slash < 0 ? reference.Length : slash).Contains(':') ? "./" + reference : reference; // "a:b" alone would read as a scheme
    }

    private static string EscapePath(string path)
    {
        var sb = new StringBuilder(path.Length + 8);
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.Value == '/' || rune.Value >= 128 || char.IsAsciiLetterOrDigit((char)rune.Value) || rune.Value is '-' or '.' or '_' or '~' or ':') sb.Append(rune.ToString());
            else
                foreach (byte b in utf8[..rune.EncodeToUtf8(utf8)]) sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The full path of the file that <paramref name="typed"/> names, as a path (absolute, or relative to <paramref name="documentDirectory"/>), or null when it names
    /// no existing file: a web address or any text with a scheme (<c>https:</c>, <c>mailto:</c>) is never taken for a file.
    /// </summary>
    public static string? LocalFile(string typed, string? documentDirectory)
    {
        int colon = typed.IndexOf(':');
        if (colon > 1 && typed[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return null;
        try
        {
            bool rooted = Path.IsPathRooted(typed);
            if (!rooted && documentDirectory is null) return null;
            string path = rooted ? typed : Path.Combine(documentDirectory!, typed);
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>A text as the value of an attribute in double quotes: <c>&amp;</c> (unless it already starts an entity), quotes and angle brackets are written as entities.</summary>
    public static string Attribute(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '&') sb.Append(StartsEntity(value, i) ? "&" : "&amp;");
            else if (c == '"') sb.Append("&quot;");
            else if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool StartsEntity(string text, int at)
    {
        int i = at + 1;
        if (i < text.Length && text[i] == '#') i++;
        int start = i;
        while (i < text.Length && char.IsAsciiLetterOrDigit(text[i])) i++;
        return i > start && i < text.Length && text[i] == ';';
    }

    /// <summary>
    /// <c>&lt;img src="…" width="…" height="…" alt="…"&gt;</c>; the size is left out when it is unknown. <see cref="Piece.Caret"/> is inside the quotes of <c>alt</c> when
    /// <paramref name="alt"/> is empty, so the text can be typed at once, and behind the tag otherwise.
    /// </summary>
    public static Piece Image(string reference, (int Width, int Height)? size, string alt = "")
    {
        var sb = new StringBuilder("<img src=\"").Append(Attribute(reference)).Append('"');
        if (size is { } s) sb.Append(" width=\"").Append(s.Width).Append("\" height=\"").Append(s.Height).Append('"');
        sb.Append(" alt=\"");
        int caret = sb.Length;
        sb.Append(Attribute(alt)).Append("\">");
        return new Piece(sb.ToString(), alt.Length == 0 ? caret : sb.Length);
    }

    /// <summary><c>&lt;a href="…"&gt;content&lt;/a&gt;</c>; <paramref name="content"/> is markup (text or an image tag) and is written as it is.</summary>
    public static Piece Link(string reference, string content)
    {
        string text = "<a href=\"" + Attribute(reference) + "\">" + content + "</a>";
        return new Piece(text, text.Length);
    }

    /// <summary>The text to write and where the caret goes in it.</summary>
    public readonly record struct Piece(string Text, int Caret);
}

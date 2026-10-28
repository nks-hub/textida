namespace NksHub.Textida.Core.Markdown;

/// <summary>
/// Recognises the raster pictures a preview or the picture viewer may draw (PNG, JPEG, GIF, BMP, WebP, ICO) by their first bytes, never by a file name, a media type or a declared type, and reads their size from the
/// header so that a small file that expands to a huge bitmap is refused before it is decoded. SVG and every other format are refused: SVG can carry script and references to other files.
/// shortcut: a picture whose size cannot be read from its header (a bitmap with the old 12-byte header, a damaged file) is refused as well; add a reader when a user needs such a file.
/// </summary>
public static class RasterImage
{
    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> Ico => [0x00, 0x00, 0x01, 0x00];

    public static bool IsSupported(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12
        && (bytes.StartsWith(Png) || bytes.StartsWith(Jpeg) || bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) || bytes.StartsWith("BM"u8)
            || (bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) || IsIcon(bytes));

    /// <summary>An icon: the header, at least one entry, and the data of the first entry after the directory. Four bytes are a weak signature, so the directory is checked too.</summary>
    private static bool IsIcon(ReadOnlySpan<byte> b) =>
        b.Length >= 22 && b.StartsWith(Ico) && (b[4] | b[5] << 8) is var count and > 0 and <= 256 && b[9] == 0 && LittleEndian(b[18..]) >= 6 + 16 * count;

    /// <summary>
    /// The name of the format a file starting with <paramref name="head"/> is a picture of, or null. Stricter than <see cref="IsSupported"/> for a bitmap, whose two letters also begin plain text:
    /// the size of the header and the one plane must be there. Meant for deciding at open whether a file is a picture; a damaged picture is found out when it is decoded.
    /// </summary>
    public static string? Detect(ReadOnlySpan<byte> head)
    {
        if (!IsSupported(head)) return null;
        if (head.StartsWith("BM"u8))
            return head.Length >= 30 && LittleEndian(head[14..]) is 40 or 52 or 56 or 64 or 108 or 124 && (head[26] | head[27] << 8) == 1 ? "BMP" : null;
        return FormatName(head);
    }

    /// <summary>The name of the format of bytes <see cref="IsSupported"/> accepts; null for any other.</summary>
    public static string? FormatName(ReadOnlySpan<byte> b)
    {
        if (!IsSupported(b)) return null;
        if (b.StartsWith(Png)) return "PNG";
        if (b.StartsWith(Jpeg)) return "JPEG";
        if (b.StartsWith("GIF"u8)) return "GIF";
        if (b.StartsWith("BM"u8)) return "BMP";
        return b.StartsWith("RIFF"u8) ? "WebP" : "ICO";
    }

    /// <summary>The width and height in pixels from the header; null for a format the preview does not draw or a header that cannot be read.</summary>
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> b)
    {
        if (!IsSupported(b)) return null;
        if (b.StartsWith(Png)) return b.Length >= 24 ? (BigEndian(b[16..]), BigEndian(b[20..])) : null;
        if (b.StartsWith("GIF"u8)) return b.Length >= 10 ? (b[6] | b[7] << 8, b[8] | b[9] << 8) : null;
        if (b.StartsWith("BM"u8)) return b.Length >= 26 && b[14] >= 40 ? (LittleEndian(b[18..]), Math.Abs(LittleEndian(b[22..]))) : null;
        if (b.StartsWith(Jpeg)) return JpegSize(b);
        if (b.StartsWith(Ico)) return IconSize(b);
        return WebpSize(b);
    }

    private static int BigEndian(ReadOnlySpan<byte> b) => b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3];

    private static int LittleEndian(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24;

    private static (int, int)? JpegSize(ReadOnlySpan<byte> b)
    {
        for (int i = 2; i + 9 < b.Length;)
        {
            if (b[i] != 0xFF) { i++; continue; }
            byte marker = b[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) return (b[i + 7] << 8 | b[i + 8], b[i + 5] << 8 | b[i + 6]);
            i += marker switch { 0xFF => 1, 0x01 or (>= 0xD0 and <= 0xD8) => 2, _ => 2 + (b[i + 2] << 8 | b[i + 3]) };
        }
        return null;
    }

    /// <summary>The largest picture of the directory of an icon; a width or height byte of 0 means 256.</summary>
    private static (int, int)? IconSize(ReadOnlySpan<byte> b)
    {
        int count = Math.Min(b[4] | b[5] << 8, (b.Length - 6) / 16);
        (int Width, int Height) best = (0, 0);
        for (int i = 0; i < count; i++)
        {
            var entry = b[(6 + 16 * i)..];
            var size = (entry[0] == 0 ? 256 : entry[0], entry[1] == 0 ? 256 : entry[1]);
            if (size.Item1 * size.Item2 > best.Width * best.Height) best = size;
        }
        return best.Width > 0 ? best : null;
    }

    private static (int, int)? WebpSize(ReadOnlySpan<byte> b)
    {
        if (b.Length < 30) return null;
        var chunk = b[12..16];
        if (chunk.SequenceEqual("VP8X"u8)) return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        if (chunk.SequenceEqual("VP8 "u8)) return b[23] == 0x9D && b[24] == 0x01 && b[25] == 0x2A ? ((b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF) : null;
        if (chunk.SequenceEqual("VP8L"u8)) return b[20] == 0x2F ? (1 + ((b[22] & 0x3F) << 8 | b[21]), 1 + ((b[24] & 0x0F) << 10 | b[23] << 2 | (b[22] & 0xC0) >> 6)) : null;
        return null;
    }
}

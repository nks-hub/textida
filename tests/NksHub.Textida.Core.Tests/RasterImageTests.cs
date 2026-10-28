using System.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.3.1: pictures are recognised by their first bytes and sized from the header, so a small file that expands to a huge bitmap is refused before it is decoded.</summary>
public class RasterImageTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static byte[] Padded(byte[] head, int length = 40)
    {
        var bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    [Fact]
    public void A_png_is_sized_from_its_header_and_a_header_that_claims_a_huge_size_is_reported_as_one()
    {
        var png = Convert.FromBase64String(OnePixelPng);
        Assert.True(RasterImage.IsSupported(png));
        Assert.Equal((1, 1), RasterImage.Size(png));

        png[16] = 0; png[17] = 0; png[18] = 0xC3; png[19] = 0x50; // 50000
        png[20] = 0; png[21] = 0; png[22] = 0xC3; png[23] = 0x50;
        Assert.Equal((50000, 50000), RasterImage.Size(png));
    }

    [Fact]
    public void Gif_bmp_and_jpeg_headers_give_their_size()
    {
        Assert.Equal((3, 2), RasterImage.Size(Padded([.. "GIF89a"u8, 3, 0, 2, 0], 16)));

        var bmp = Padded([.. "BM"u8], 54);
        bmp[14] = 40;
        BitConverter.GetBytes(640).CopyTo(bmp, 18);
        BitConverter.GetBytes(-480).CopyTo(bmp, 22); // a top-down bitmap has a negative height
        Assert.Equal((640, 480), RasterImage.Size(bmp));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0x01, 0x11, 0x00];
        Assert.Equal((640, 480), RasterImage.Size(jpeg));
    }

    [Fact]
    public void The_three_kinds_of_webp_give_their_size()
    {
        var extended = Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0, 0x1F, 0x03, 0x00, 0x57, 0x02, 0x00]);
        Assert.Equal((800, 600), RasterImage.Size(extended));

        var lossless = Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8L"u8, 0, 0, 0, 0, 0x2F, 0x63, 0x40, 0x0C, 0x00]);
        Assert.Equal((100, 50), RasterImage.Size(lossless));

        var lossy = Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0, 0, 0, 0, 0x9D, 0x01, 0x2A, 0x40, 0x01, 0xC8, 0x00]);
        Assert.Equal((320, 200), RasterImage.Size(lossy));
    }

    [Fact]
    public void Svg_text_other_formats_and_short_input_are_refused()
    {
        Assert.False(RasterImage.IsSupported(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>1</script></svg>")));
        Assert.Null(RasterImage.Size(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        Assert.False(RasterImage.IsSupported([0x89, 0x50, 0x4E, 0x47]));
        Assert.False(RasterImage.IsSupported([]));
        Assert.False(RasterImage.IsSupported(Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WAVEfmt "u8])));
        Assert.Null(RasterImage.Size(Padded([.. "BM"u8], 30))); // an old bitmap header of 12 bytes has no 32-bit size
    }

    private static byte[] Icon(params (int Width, int Height)[] entries)
    {
        var bytes = new byte[6 + 16 * entries.Length + 8];
        bytes[2] = 1;
        bytes[4] = (byte)entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            int at = 6 + 16 * i;
            bytes[at] = (byte)(entries[i].Width == 256 ? 0 : entries[i].Width);
            bytes[at + 1] = (byte)(entries[i].Height == 256 ? 0 : entries[i].Height);
            BitConverter.GetBytes(6 + 16 * entries.Length).CopyTo(bytes, at + 12);
        }
        return bytes;
    }

    [Fact]
    public void An_icon_is_sized_by_its_largest_entry_and_four_zero_bytes_alone_are_not_an_icon()
    {
        var ico = Icon((16, 16), (256, 256), (32, 32));
        Assert.True(RasterImage.IsSupported(ico));
        Assert.Equal((256, 256), RasterImage.Size(ico));
        Assert.Equal("ICO", RasterImage.FormatName(ico));
        Assert.Equal("ICO", RasterImage.Detect(ico));

        var notIcon = Icon((16, 16));
        BitConverter.GetBytes(2).CopyTo(notIcon, 18); // the data would start inside the directory
        Assert.False(RasterImage.IsSupported(notIcon));
        Assert.False(RasterImage.IsSupported(Padded([0, 0, 1, 0, 0, 0], 64))); // no entry
        Assert.False(RasterImage.IsSupported(Padded([0, 0, 2, 0, 1, 0], 64))); // a cursor
    }

    [Fact]
    public void Every_format_has_a_name_and_a_bitmap_needs_a_real_header_to_be_detected_while_plain_text_with_the_same_two_letters_is_not_a_picture()
    {
        var png = Convert.FromBase64String(OnePixelPng);
        Assert.Equal("PNG", RasterImage.FormatName(png));
        Assert.Equal("PNG", RasterImage.Detect(png));
        Assert.Equal("GIF", RasterImage.FormatName(Padded([.. "GIF89a"u8, 3, 0, 2, 0], 16)));
        Assert.Equal("JPEG", RasterImage.Detect(Padded([0xFF, 0xD8, 0xFF, 0xE0], 40)));
        Assert.Equal("WebP", RasterImage.FormatName(Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8L"u8, 0, 0, 0, 0, 0x2F, 0x63, 0x40, 0x0C, 0x00])));
        Assert.Null(RasterImage.FormatName(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));

        var bmp = Padded([.. "BM"u8], 54);
        bmp[14] = 40;
        bmp[26] = 1;
        BitConverter.GetBytes(4).CopyTo(bmp, 18);
        BitConverter.GetBytes(3).CopyTo(bmp, 22);
        Assert.Equal("BMP", RasterImage.Detect(bmp));
        Assert.Null(RasterImage.Detect(Encoding.UTF8.GetBytes("BMW is a car maker, and this is a plain text file that happens to start with two letters.")));
    }
}

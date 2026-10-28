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
}

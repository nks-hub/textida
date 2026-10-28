using System.Buffers.Binary;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.6.5: the size of an image file, the relative address of a file and the tags of Insert Image and Insert Link.</summary>
public class HtmlInsertTests
{
    private static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        return b;
    }

    private static byte[] Gif(int w, int h)
    {
        var b = new byte[13];
        "GIF89a"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), (ushort)h);
        return b;
    }

    private static byte[] Bmp(int w, int h, uint header = 40)
    {
        var b = new byte[54];
        "BM"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14), header);
        if (header == 12)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), (ushort)w);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20), (ushort)h);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), h);
        }
        return b;
    }

    private static byte[] WebP(string kind, int w, int h)
    {
        var b = new byte[40];
        "RIFF"u8.CopyTo(b);
        "WEBP"u8.CopyTo(b.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(kind).CopyTo(b, 12);
        switch (kind)
        {
            case "VP8 ":
                new byte[] { 0x9D, 0x01, 0x2A }.CopyTo(b, 23);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), (ushort)(w | 0x4000)); // the two top bits are a scale, not part of the size
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28), (ushort)h);
                break;
            case "VP8L":
                b[20] = 0x2F;
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(21), (uint)(w - 1) | (uint)(h - 1) << 14);
                break;
            default: // VP8X
                b[24] = (byte)(w - 1); b[25] = (byte)(w - 1 >> 8); b[26] = (byte)(w - 1 >> 16);
                b[27] = (byte)(h - 1); b[28] = (byte)(h - 1 >> 8); b[29] = (byte)(h - 1 >> 16);
                break;
        }
        return b;
    }

    /// <summary>A JPEG with an APP0 and a comment segment, then the marker given and a frame header of that size (the data after it is not needed).</summary>
    private static byte[] Jpeg(byte frameMarker, int w, int h)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange(new byte[14]);
        bytes.AddRange(new byte[] { 0xFF, 0xFF, 0xFE, 0x00, 0x05, 1, 2, 3 }); // a fill byte before the marker, then a 5-byte comment
        bytes.AddRange(new byte[] { 0xFF, frameMarker, 0x00, 0x11, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3 });
        bytes.AddRange(new byte[10]);
        return [.. bytes];
    }

    private static (bool Ok, int Width, int Height) Size(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return (HtmlInsert.TryImageSize(stream, out int w, out int h), w, h);
    }

    [Fact]
    public void The_size_comes_from_the_header_of_each_kind_of_image()
    {
        Assert.Equal((true, 640, 480), Size(Png(640, 480)));
        Assert.Equal((true, 70000, 3), Size(Png(70000, 3)));
        Assert.Equal((true, 123, 45), Size(Gif(123, 45)));
        Assert.Equal((true, 200, 100), Size(Bmp(200, 100)));
        Assert.Equal((true, 200, 100), Size(Bmp(200, -100)));
        Assert.Equal((true, 17, 9), Size(Bmp(17, 9, header: 12)));
        Assert.Equal((true, 321, 123), Size(WebP("VP8 ", 321, 123)));
        Assert.Equal((true, 321, 123), Size(WebP("VP8L", 321, 123)));
        Assert.Equal((true, 4000, 3000), Size(WebP("VP8X", 4000, 3000)));
        Assert.Equal((true, 1920, 1080), Size(Jpeg(0xC0, 1920, 1080)));
        Assert.Equal((true, 640, 480), Size(Jpeg(0xC2, 640, 480))); // progressive
    }

    [Fact]
    public void A_real_one_by_one_png_and_gif_are_read()
    {
        Assert.Equal((true, 1, 1), Size(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")));
        Assert.Equal((true, 1, 1), Size(Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")));
    }

    [Fact]
    public void Anything_else_and_a_damaged_header_have_no_size()
    {
        Assert.False(Size([]).Ok);
        Assert.False(Size("plain text, not an image"u8.ToArray()).Ok);
        Assert.False(Size("<svg width=\"10\" height=\"10\"></svg>"u8.ToArray()).Ok);
        Assert.False(Size(Png(0, 5)).Ok);
        Assert.False(Size(Png(-1, 5)).Ok);
        Assert.False(Size(Png(640, 480)[..20]).Ok);
        Assert.False(Size(Gif(0, 0)).Ok);
        Assert.False(Size(Jpeg(0xC4, 10, 10)).Ok); // a Huffman table is not a frame header
        Assert.False(Size([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0x00, 0x00]).Ok); // the scan starts before any frame header
        Assert.False(Size(Jpeg(0xC0, 10, 10)[..33]).Ok); // cut inside the frame header
        Assert.False(Size(WebP("VP8L", 5, 5).Select((b, i) => i == 20 ? (byte)0 : b).ToArray()).Ok); // no signature byte
    }

    [Fact]
    public void A_jpeg_in_a_stream_that_cannot_seek_is_not_measured_but_a_png_is()
    {
        using var png = new ForwardOnly(Png(30, 20));
        Assert.True(HtmlInsert.TryImageSize(png, out int w, out int h));
        Assert.Equal((30, 20), (w, h));
        using var jpeg = new ForwardOnly(Jpeg(0xC0, 30, 20));
        Assert.False(HtmlInsert.TryImageSize(jpeg, out _, out _));
    }

    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public void Image_files_are_told_by_their_extension()
    {
        Assert.True(HtmlInsert.IsImageFile("a/b/Photo.JPG") && HtmlInsert.IsImageFile("x.webp") && HtmlInsert.IsImageFile("y.png"));
        Assert.False(HtmlInsert.IsImageFile("a.svg") || HtmlInsert.IsImageFile("a.txt") || HtmlInsert.IsImageFile("png"));
    }

    [Fact]
    public void A_file_is_written_as_a_relative_reference_with_the_unsafe_characters_escaped()
    {
        string root = Path.Combine(Path.GetTempPath(), "html-insert-root");
        string pages = Path.Combine(root, "site", "pages");
        Assert.Equal("a.png", HtmlInsert.RelativeReference(pages, Path.Combine(pages, "a.png")));
        Assert.Equal("img/a.png", HtmlInsert.RelativeReference(pages, Path.Combine(pages, "img", "a.png")));
        Assert.Equal("../img/my%20pic%231%26%27.png", HtmlInsert.RelativeReference(pages, Path.Combine(root, "site", "img", "my pic#1&'.png")));
        Assert.Equal("../../obrázek.png", HtmlInsert.RelativeReference(pages, Path.Combine(root, "obrázek.png"))); // letters beyond ASCII stay readable
        Assert.Equal("%25.png", HtmlInsert.RelativeReference(pages, Path.Combine(pages, "%.png")));
    }

    [Fact]
    public void A_file_with_no_folder_to_start_from_is_written_as_a_file_address()
    {
        string path = Path.Combine(Path.GetTempPath(), "html-insert-root", "a b.png");
        string reference = HtmlInsert.RelativeReference(null, path);
        Assert.StartsWith("file:///", reference);
        Assert.EndsWith("/a%20b.png", reference);
        Assert.DoesNotContain('\\', reference);
    }

    [Fact]
    public void A_first_segment_with_a_colon_does_not_read_as_a_scheme()
    {
        if (OperatingSystem.IsWindows()) return; // a colon is not allowed in a Windows file name
        string pages = Path.Combine(Path.GetTempPath(), "html-insert-root", "pages");
        Assert.Equal("./a:b/c.png", HtmlInsert.RelativeReference(pages, Path.Combine(pages, "a:b", "c.png")));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a&b", "a&amp;b")]
    [InlineData("a &amp; b &#38; c &copy; d", "a &amp; b &#38; c &copy; d")]
    [InlineData("&; & ;", "&amp;; &amp; ;")]
    [InlineData("say \"hi\" <b>", "say &quot;hi&quot; &lt;b&gt;")]
    public void Attribute_values_escape_what_would_end_the_attribute_or_start_markup(string value, string expected) => Assert.Equal(expected, HtmlInsert.Attribute(value));

    [Fact]
    public void An_image_tag_has_the_size_when_it_is_known_and_the_caret_in_an_empty_alt()
    {
        var piece = HtmlInsert.Image("img/a.png", (640, 480));
        Assert.Equal("<img src=\"img/a.png\" width=\"640\" height=\"480\" alt=\"\">", piece.Text);
        Assert.Equal("<img src=\"img/a.png\" width=\"640\" height=\"480\" alt=\"".Length, piece.Caret);

        var unknown = HtmlInsert.Image("a.svg", null);
        Assert.Equal("<img src=\"a.svg\" alt=\"\">", unknown.Text);

        var described = HtmlInsert.Image("a.png", (1, 2), "A \"quoted\" photo");
        Assert.Equal("<img src=\"a.png\" width=\"1\" height=\"2\" alt=\"A &quot;quoted&quot; photo\">", described.Text);
        Assert.Equal(described.Text.Length, described.Caret);
    }

    [Fact]
    public void A_link_wraps_its_content_as_it_is_and_escapes_the_address()
    {
        var piece = HtmlInsert.Link("https://example.org/?a=1&b=2", "see <b>this</b>");
        Assert.Equal("<a href=\"https://example.org/?a=1&amp;b=2\">see <b>this</b></a>", piece.Text);
        Assert.Equal(piece.Text.Length, piece.Caret);
    }

    [Fact]
    public void A_typed_address_is_a_local_file_only_when_that_file_exists()
    {
        var dir = Directory.CreateTempSubdirectory("textida-html-");
        try
        {
            string file = Path.Combine(dir.FullName, "pic one.png");
            File.WriteAllBytes(file, Png(2, 2));
            Assert.Equal(file, HtmlInsert.LocalFile("pic one.png", dir.FullName));
            Assert.Equal(file, HtmlInsert.LocalFile(file, null));
            Assert.Equal(file, HtmlInsert.LocalFile(file, @"X:\elsewhere"));
            Assert.Null(HtmlInsert.LocalFile("pic one.png", null)); // an unsaved document has no folder to look in
            Assert.Null(HtmlInsert.LocalFile("missing.png", dir.FullName));
            Assert.Null(HtmlInsert.LocalFile("https://example.org/pic one.png", dir.FullName));
            Assert.Null(HtmlInsert.LocalFile("mailto:me@example.org", dir.FullName));
            Assert.Null(HtmlInsert.LocalFile("bad\0name", dir.FullName));
            Assert.Null(HtmlInsert.LocalFile(dir.FullName, null)); // a folder is not a file
        }
        finally { dir.Delete(recursive: true); }
    }
}

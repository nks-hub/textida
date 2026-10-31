using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.5.3: the single-byte code pages. Each is a table of 256 characters, so every byte survives a trip through text and back, and a block cut anywhere decodes the same.</summary>
public class SingleBytePagesTests
{
    private static readonly byte[] AllBytes = [.. Enumerable.Range(0, 256).Select(b => (byte)b)];

    public static IEnumerable<object[]> Pages => TextCodec.All.Where(c => c.Kind == CodecKind.SingleByte).Select(c => new object[] { c.Name });

    [Fact]
    public void The_list_has_every_page_of_the_scope_once_under_a_group()
    {
        var names = TextCodec.All.Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        string[] expected =
        [
            "Windows-1250", "Windows-1251", "Windows-1252", "Windows-1253", "Windows-1254", "Windows-1255", "Windows-1256", "Windows-1257", "Windows-1258",
            "ISO-8859-1", "ISO-8859-2", "ISO-8859-3", "ISO-8859-4", "ISO-8859-5", "ISO-8859-6", "ISO-8859-7", "ISO-8859-8", "ISO-8859-9", "ISO-8859-10", "ISO-8859-11",
            "ISO-8859-13", "ISO-8859-14", "ISO-8859-15", "ISO-8859-16", "KOI8-R", "KOI8-U", "DOS CP437", "DOS CP850", "DOS CP852", "DOS CP866", "Mac Roman", "Mac Central European", "Kamenický (CP895)",
        ];
        Assert.All(expected, name => Assert.NotNull(TextCodec.ByName(name)));
        Assert.DoesNotContain(names, n => n == "ISO-8859-12");
        Assert.All(TextCodec.All, c => Assert.True(Enum.IsDefined(c.Group)));
        Assert.All(TextCodec.All.Where(c => c.Kind != CodecKind.SingleByte), c => Assert.Equal(EncodingGroup.Unicode, c.Group));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_byte_goes_to_a_text_and_back_to_the_same_byte(string name)
    {
        var codec = TextCodec.ByName(name)!;
        var decoded = codec.Decode(AllBytes);
        Assert.Equal(256, decoded.Text.Length);
        Assert.Equal(256, decoded.Text.Distinct().Count()); // no two bytes share a character, so none is lost on the way back
        Assert.Equal(AllBytes, codec.Encode(decoded.Text));
        foreach (var block in new[] { 4, 7, 64 })
            Assert.Equal(decoded.Text, codec.DecodeInBlocks(AllBytes, block).Text);
        Assert.True(codec.IsLossy);
        Assert.False(codec.CanEncode(0x4E2D)); // a CJK character is not in any of them
    }

    [Theory]
    [InlineData("Windows-1251", 0xC0, 'А')]
    [InlineData("KOI8-R", 0xC1, 'а')]
    [InlineData("KOI8-U", 0xA4, 'є')]
    [InlineData("DOS CP866", 0x80, 'А')]
    [InlineData("ISO-8859-5", 0xB0, 'А')]
    [InlineData("Windows-1253", 0xC1, 'Α')]
    [InlineData("ISO-8859-7", 0xE1, 'α')]
    [InlineData("Windows-1254", 0xD0, 'Ğ')]
    [InlineData("ISO-8859-9", 0xDD, 'İ')]
    [InlineData("Windows-1255", 0xE0, 'א')]
    [InlineData("Windows-1256", 0xC7, 'ا')]
    [InlineData("Windows-1257", 0xC0, 'Ą')]
    [InlineData("ISO-8859-13", 0xC0, 'Ą')]
    [InlineData("ISO-8859-4", 0xA1, 'Ą')]
    [InlineData("ISO-8859-10", 0xA1, 'Ą')]
    [InlineData("ISO-8859-16", 0xA1, 'Ą')]
    [InlineData("ISO-8859-14", 0xA1, 'Ḃ')]
    [InlineData("ISO-8859-15", 0xA4, '€')]
    [InlineData("ISO-8859-11", 0xA1, 'ก')]
    [InlineData("ISO-8859-11", 0xA0, ' ')]
    [InlineData("ISO-8859-1", 0xE9, 'é')]
    [InlineData("DOS CP437", 0x80, 'Ç')]
    [InlineData("DOS CP852", 0xD8, 'ě')]
    [InlineData("Mac Roman", 0x80, 'Ä')]
    [InlineData("Mac Central European", 0x80, 'Ä')]
    [InlineData("Windows-1258", 0xC3, 'Ă')]
    public void Known_bytes_are_the_characters_of_their_page(string name, int value, char expected)
    {
        var text = TextCodec.ByName(name)!.Decode(new[] { (byte)value }).Text;
        Assert.Equal(expected.ToString(), text);
    }

    [Fact]
    public void A_character_a_page_cannot_hold_is_reported_as_a_loss_and_never_replaced()
    {
        var ex = Assert.Throws<UnrepresentableCharactersException>(() => TextCodec.ByName("Windows-1251")!.Encode("Привет ř"));
        Assert.Equal(1, ex.Report.Total);
        Assert.Equal([0xCF, 0xF0], TextCodec.ByName("Windows-1251")!.Encode("Пр").Select(b => (int)b));
    }

    [Fact]
    public void Two_pages_are_the_same_bytes_only_when_they_are_the_same_page()
    {
        Assert.True(TextCodec.Cp1250.SameBytesAs(TextCodec.Cp1250));
        Assert.False(TextCodec.ByName("ISO-8859-10")!.SameBytesAs(TextCodec.Kamenicky)); // both have no .NET number
        Assert.False(TextCodec.ByName("ISO-8859-14")!.SameBytesAs(TextCodec.ByName("ISO-8859-16")!));
    }
}

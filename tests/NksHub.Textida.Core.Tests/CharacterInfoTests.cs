using System.Globalization;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Tests;

public class CharacterInfoTests
{
    [Theory]
    [InlineData(0x41, "LATIN CAPITAL LETTER A")]
    [InlineData(0xE9, "LATIN SMALL LETTER E WITH ACUTE")]
    [InlineData(0x20AC, "EURO SIGN")]
    [InlineData(0x1F600, "GRINNING FACE")]
    [InlineData(0x0A, "LINE FEED")]
    [InlineData(0x7F, "DELETE")]
    [InlineData(0x4E2D, "CJK UNIFIED IDEOGRAPH-4E2D")] // the name of an ideograph is made of its code point
    [InlineData(0xAC00, "HANGUL SYLLABLE GA")]
    [InlineData(0xD55C, "HANGUL SYLLABLE HAN")]
    [InlineData(0x664, "ARABIC-INDIC DIGIT FOUR")]
    public void A_character_has_its_standard_name(int codePoint, string name) => Assert.Equal(name, CharacterInfo.NameOf(codePoint));

    [Fact]
    public void A_code_point_without_a_name_has_none()
    {
        Assert.Null(CharacterInfo.NameOf(0xE000)); // private use
        Assert.Null(CharacterInfo.NameOf(0xD800)); // a surrogate
        Assert.Null(CharacterInfo.NameOf(0x378)); // unassigned
    }

    [Fact]
    public void The_bytes_and_the_category_are_those_of_the_character_in_UTF8_and_UTF16()
    {
        var e = CharacterInfo.At("café!", 3)!;
        Assert.Equal(("U+00E9", "Ll", "C3 A9", "00 E9"), (e.CodePointText, e.CategoryCode, CharacterInfo.Hex(e.Utf8), CharacterInfo.Hex(e.Utf16)));

        var smile = CharacterInfo.At("a\U0001F600b", 1)!; // a pair of surrogates is one character
        Assert.Equal(("U+1F600", "So", "F0 9F 98 80", "D8 3D DE 00"), (smile.CodePointText, smile.CategoryCode, CharacterInfo.Hex(smile.Utf8), CharacterInfo.Hex(smile.Utf16)));

        var lone = CharacterInfo.At("\uD800x", 0)!;
        Assert.Empty(lone.Utf8); // a lone surrogate has no UTF-8
        Assert.Equal("Cs", lone.CategoryCode);
        Assert.Null(CharacterInfo.At("ab", 2));
        Assert.Equal(UnicodeCategory.DecimalDigitNumber, CharacterInfo.At("7", 0)!.Category);
    }
}

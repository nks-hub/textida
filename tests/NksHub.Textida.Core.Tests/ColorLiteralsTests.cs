using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.Core.Tests;

public class ColorLiteralsTests
{
    private static ColorMatch One(string literal)
    {
        Assert.True(ColorLiterals.TryParse(literal, out var match), literal);
        return match;
    }

    [Fact]
    public void Colours_are_found_in_every_notation_with_their_place()
    {
        const string css = "a { color: #ff8800; b: rgb(255 0 0 / 50%); c: hsl(120, 100%, 50%); d: oklch(62.8% 0.2577 29.23); e: RGBA(0,0,255,0.25); f: #FFF }";
        var found = ColorLiterals.Find(css);
        Assert.Equal(["#ff8800", "rgb(255 0 0 / 50%)", "hsl(120, 100%, 50%)", "oklch(62.8% 0.2577 29.23)", "RGBA(0,0,255,0.25)", "#FFF"], found.Select(m => css.Substring(m.Start, m.Length)));
        Assert.Equal([ColorFormat.Hex, ColorFormat.Rgb, ColorFormat.Hsl, ColorFormat.Oklch, ColorFormat.Rgb, ColorFormat.Hex], found.Select(m => m.Format));
        Assert.Equal(new Rgba(255, 136, 0), found[0].Color);
        Assert.Equal(new Rgba(255, 0, 0, 0.5), found[1].Color);
        Assert.Equal(new Rgba(0, 255, 0), found[2].Color);
        Assert.Equal(new Rgba(255, 0, 0), found[3].Color); // oklch(62.8% 0.2577 29.23) is sRGB red
        Assert.Equal(new Rgba(0, 0, 255, 0.25), found[4].Color);
        Assert.Equal(new Rgba(255, 255, 255), found[5].Color);
        Assert.True(found[5].UpperHex);
    }

    [Theory]
    [InlineData("&#123;")]
    [InlineData("issue#1234567")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#abcdefg")]
    [InlineData("rgb(var(--c))")]
    [InlineData("rgb(1 2)")]
    [InlineData("rgb(1 2 3 4 5)")]
    [InlineData("rgb(10px 2 3)")]
    [InlineData("hsl(none 50% 50%)")]
    [InlineData("xrgb(1 2 3)")]
    [InlineData("rgb(1 2 3")]
    public void Text_that_only_looks_like_a_colour_is_not_one(string text) => Assert.Empty(ColorLiterals.Find(text));

    [Fact]
    public void Hex_values_give_the_hex_digits_and_the_opacity()
    {
        Assert.Equal(new Rgba(0x11, 0x22, 0x33), One("#123").Color);
        Assert.Equal(new Rgba(0x11, 0x22, 0x33, 0x44 / 255.0), One("#1234").Color);
        Assert.Equal(new Rgba(0xAB, 0xCD, 0xEF, 0x80 / 255.0), One("#abcdef80").Color);
        Assert.Equal("#abcdef80", ColorLiterals.Format(One("#abcdef80").Color, ColorFormat.Hex));
        Assert.Equal("#ABCDEF", ColorLiterals.FormatLike(One("#ABCDEF"), new Rgba(0xAB, 0xCD, 0xEF)));
    }

    [Fact]
    public void Red_in_oklch_is_the_value_the_standard_gives()
    {
        var (l, c, h) = ColorLiterals.ToOklch(new Rgba(255, 0, 0));
        Assert.Equal(0.6280, l, 3);
        Assert.Equal(0.2577, c, 3);
        Assert.Equal(29.23, h, 1);
        Assert.Equal("oklch(0.62796 0.25768 29.23)", ColorLiterals.Format(new Rgba(255, 0, 0), ColorFormat.Oklch));
        Assert.Equal("oklch(1 0 0)", ColorLiterals.Format(new Rgba(255, 255, 255), ColorFormat.Oklch));
        Assert.Equal("oklch(0 0 0)", ColorLiterals.Format(new Rgba(0, 0, 0), ColorFormat.Oklch));
    }

    [Fact]
    public void Converting_ff8800_to_oklch_keeps_the_colour()
    {
        var orange = One("#ff8800");
        string oklch = ColorLiterals.Format(orange.Color, ColorFormat.Oklch);
        Assert.StartsWith("oklch(0.7", oklch);
        Assert.Equal(orange.Color, One(oklch).Color);
        Assert.Equal("#ff8800", ColorLiterals.Format(One(oklch).Color, ColorFormat.Hex));
    }

    [Fact]
    public void Every_eight_bit_colour_on_a_grid_survives_all_four_notations()
    {
        for (int r = 0; r < 256; r += 5)
            for (int g = 0; g < 256; g += 5)
                for (int b = 0; b < 256; b += 5)
                    foreach (var format in new[] { ColorFormat.Hex, ColorFormat.Rgb, ColorFormat.Hsl, ColorFormat.Oklch })
                    {
                        var color = new Rgba((byte)r, (byte)g, (byte)b);
                        string text = ColorLiterals.Format(color, format);
                        Assert.True(ColorLiterals.TryParse(text, out var back), text);
                        Assert.True(back.Color.SameRgb(color), $"{format}: {text} gave {back.Color}, not {color}");
                    }
    }

    [Fact]
    public void Edge_and_random_colours_survive_oklch_exactly()
    {
        var random = new Random(7);
        int[] edges = [0, 1, 2, 3, 127, 128, 252, 253, 254, 255];
        int Pick() => random.Next(3) == 0 ? random.Next(256) : edges[random.Next(edges.Length)];
        for (int i = 0; i < 200_000; i++)
        {
            var color = new Rgba((byte)Pick(), (byte)Pick(), (byte)Pick());
            string text = ColorLiterals.Format(color, ColorFormat.Oklch);
            Assert.True(ColorLiterals.TryParse(text, out var back), text);
            Assert.True(back.Color.SameRgb(color), $"{text} gave {back.Color}, not {color}");
        }
    }

    [Fact]
    public void Opacity_is_written_and_read_in_every_notation()
    {
        var half = One("#ff880080").Color;
        Assert.Equal("rgb(255 136 0 / 0.502)", ColorLiterals.Format(half, ColorFormat.Rgb));
        Assert.Equal("hsl(32 100% 50% / 0.502)", ColorLiterals.Format(half, ColorFormat.Hsl));
        foreach (var format in new[] { ColorFormat.Rgb, ColorFormat.Hsl, ColorFormat.Oklch })
            Assert.Equal("#ff880080", ColorLiterals.Format(One(ColorLiterals.Format(half, format)).Color, ColorFormat.Hex));
    }

    [Fact]
    public void A_colour_outside_the_srgb_gamut_is_clipped_not_wrapped()
    {
        var wild = One("oklch(2 0.5 120)").Color;
        Assert.Equal(255, wild.G);
    }
}

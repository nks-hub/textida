using System.Globalization;

namespace NksHub.Textida.Core.Colors;

/// <summary>The notations of a colour value in CSS. <see cref="Oklch"/> is the perceptual one: <c>oklch(L C H)</c> with L in 0..1, C about 0..0.4 and H in degrees.</summary>
public enum ColorFormat { Hex, Rgb, Hsl, Oklch }

/// <summary>A colour in sRGB with an opacity from 0 (clear) to 1.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, double A = 1)
{
    public bool SameRgb(Rgba other) => R == other.R && G == other.G && B == other.B;
}

/// <summary>A colour value found in a text: where it is (UTF-16 units), what colour it is and how it is written.</summary>
/// <param name="UpperHex">A hex value that was written with capital letters; a new value keeps the habit.</param>
public readonly record struct ColorMatch(int Start, int Length, Rgba Color, ColorFormat Format, bool UpperHex = false)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds colour values in text (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, <c>hsla()</c>, <c>oklch()</c>, in the comma and in the
/// space syntax, with an optional <c>/ alpha</c>), and writes a colour in any of the four notations. A value that holds a <c>var()</c>, a keyword like <c>none</c> or a nested
/// call is not a colour here. Colours outside the sRGB gamut are brought into it by clipping each channel. Used by the editor for the swatch and the picker and
/// by the colour plugin for the conversion, so both read and write the same way.
/// </summary>
public static class ColorLiterals
{
    private static bool Hex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool Word(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    /// <summary>The colour values of a text in order of position.</summary>
    public static List<ColorMatch> Find(ReadOnlySpan<char> text)
    {
        var found = new List<ColorMatch>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '#' && TryHex(text, i, out var hex))
            {
                found.Add(hex);
                i = hex.End - 1;
            }
            else if (c is 'r' or 'R' or 'h' or 'H' or 'o' or 'O' && (i == 0 || !Word(text[i - 1])) && TryFunction(text, i, out var call))
            {
                found.Add(call);
                i = call.End - 1;
            }
        }
        return found;
    }

    /// <summary>Reads <paramref name="literal"/> as exactly one colour value.</summary>
    public static bool TryParse(string literal, out ColorMatch match)
    {
        match = default;
        var all = Find(literal);
        if (all.Count != 1 || all[0].Start != 0 || all[0].Length != literal.Length) return false;
        match = all[0];
        return true;
    }

    private static bool TryHex(ReadOnlySpan<char> text, int at, out ColorMatch match)
    {
        match = default;
        if (at > 0 && (Word(text[at - 1]) || text[at - 1] is '&' or '#')) return false;
        int end = at + 1;
        while (end < text.Length && Hex(text[end])) end++;
        int digits = end - at - 1;
        if (digits is not (3 or 4 or 6 or 8) || (end < text.Length && Word(text[end]))) return false;
        string hex = text.Slice(at + 1, digits).ToString();
        int Value(int i) => Convert.ToInt32(hex[i].ToString(), 16);
        byte Channel(int index) => digits <= 4 ? (byte)(Value(index) * 17) : (byte)(Value(index * 2) * 16 + Value(index * 2 + 1));
        double alpha = digits is 4 or 8 ? Channel(3) / 255.0 : 1;
        bool upper = hex.Any(ch => ch is >= 'A' and <= 'F');
        match = new ColorMatch(at, end - at, new Rgba(Channel(0), Channel(1), Channel(2), alpha), ColorFormat.Hex, upper);
        return true;
    }

    private static bool TryFunction(ReadOnlySpan<char> text, int at, out ColorMatch match)
    {
        match = default;
        int open = at;
        while (open < text.Length && char.IsAsciiLetter(text[open])) open++;
        if (open >= text.Length || text[open] != '(') return false;
        var name = text[at..open];
        ColorFormat format;
        if (name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)) format = ColorFormat.Rgb;
        else if (name.Equals("hsl", StringComparison.OrdinalIgnoreCase) || name.Equals("hsla", StringComparison.OrdinalIgnoreCase)) format = ColorFormat.Hsl;
        else if (name.Equals("oklch", StringComparison.OrdinalIgnoreCase)) format = ColorFormat.Oklch;
        else return false;
        int close = text[(open + 1)..].IndexOfAny('(', ')');
        if (close < 0 || text[open + 1 + close] != ')') return false;
        close += open + 1;

        var values = new List<string>(4);
        bool slash = false;
        var token = new System.Text.StringBuilder();
        void Flush()
        {
            if (token.Length > 0) values.Add(token.ToString());
            token.Clear();
        }
        foreach (char c in text[(open + 1)..close])
        {
            if (c is ',' or ' ' or '\t' or '\r' or '\n') Flush();
            else if (c == '/')
            {
                if (slash || values.Count + (token.Length > 0 ? 1 : 0) != 3) return false;
                Flush();
                slash = true;
            }
            else token.Append(c);
        }
        Flush();
        if (values.Count is not (3 or 4) || (slash && values.Count != 4)) return false;

        double alpha = 1;
        if (values.Count == 4)
        {
            if (!Number(values[3], out alpha, out string unit) || unit is not ("" or "%")) return false;
            alpha = Math.Clamp(unit == "%" ? alpha / 100 : alpha, 0, 1);
        }
        Rgba color;
        switch (format)
        {
            case ColorFormat.Rgb:
                if (!Channel255(values[0], out byte r) || !Channel255(values[1], out byte g) || !Channel255(values[2], out byte b)) return false;
                color = new Rgba(r, g, b, alpha);
                break;
            case ColorFormat.Hsl:
                if (!Hue(values[0], out double h) || !Number(values[1], out double s, out string su) || !Number(values[2], out double l, out string lu) || su is not ("" or "%") || lu is not ("" or "%")) return false;
                color = FromHsl(h, Math.Clamp(s / 100, 0, 1), Math.Clamp(l / 100, 0, 1), alpha);
                break;
            default:
                if (!Number(values[0], out double lightness, out string lightUnit) || !Number(values[1], out double chroma, out string chromaUnit) || !Hue(values[2], out double hue)
                    || lightUnit is not ("" or "%") || chromaUnit is not ("" or "%")) return false;
                color = FromOklch(lightUnit == "%" ? lightness / 100 : lightness, chromaUnit == "%" ? chroma / 100 * 0.4 : chroma, hue, alpha);
                break;
        }
        match = new ColorMatch(at, close + 1 - at, color, format);
        return true;
    }

    /// <summary>A number with an optional unit; false when it is not one (a keyword, a <c>var()</c>).</summary>
    private static bool Number(string token, out double value, out string unit)
    {
        value = 0;
        int end = token.Length;
        while (end > 0 && (char.IsAsciiLetter(token[end - 1]) || token[end - 1] == '%')) end--;
        unit = token[end..].ToLowerInvariant();
        return end > 0 && double.TryParse(token.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static bool Channel255(string token, out byte value)
    {
        value = 0;
        if (!Number(token, out double v, out string unit) || unit is not ("" or "%")) return false;
        value = (byte)Math.Clamp(Math.Round(unit == "%" ? v * 2.55 : v, MidpointRounding.AwayFromZero), 0, 255);
        return true;
    }

    private static bool Hue(string token, out double degrees)
    {
        degrees = 0;
        if (!Number(token, out double v, out string unit)) return false;
        double factor;
        switch (unit)
        {
            case "" or "deg": factor = 1; break;
            case "turn": factor = 360; break;
            case "grad": factor = 0.9; break;
            case "rad": factor = 180 / Math.PI; break;
            default: return false;
        }
        degrees = ((v * factor % 360) + 360) % 360;
        return true;
    }

    // ---- conversion ----

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double FromLinear(double c) => c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    private static byte Byte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255, MidpointRounding.AwayFromZero), 0, 255);

    public static Rgba FromHsl(double hue, double saturation, double lightness, double alpha = 1)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation, h = hue / 60, x = c * (1 - Math.Abs(h % 2 - 1)), m = lightness - c / 2;
        var (r, g, b) = (int)Math.Floor(h) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgba(Byte(r + m), Byte(g + m), Byte(b + m), alpha);
    }

    public static (double H, double S, double L) ToHsl(Rgba color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2;
        if (d == 0) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static (double L, double C, double H) ToOklch(Rgba color)
    {
        double r = ToLinear(color.R / 255.0), g = ToLinear(color.G / 255.0), b = ToLinear(color.B / 255.0);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double lab = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double chroma = Math.Sqrt(a * a + bb * bb);
        double hue = chroma < 1e-4 ? 0 : (Math.Atan2(bb, a) * 180 / Math.PI + 360) % 360;
        return (lab, chroma, hue);
    }

    public static Rgba FromOklch(double lightness, double chroma, double hue, double alpha = 1)
    {
        double a = chroma * Math.Cos(hue * Math.PI / 180), b = chroma * Math.Sin(hue * Math.PI / 180);
        double l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        double m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        double s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        return new Rgba(Byte(FromLinear(Math.Clamp(r, 0, 1))), Byte(FromLinear(Math.Clamp(g, 0, 1))), Byte(FromLinear(Math.Clamp(bl, 0, 1))), alpha);
    }

    // ---- writing ----

    private static string Num(double value, int decimals)
    {
        string text = Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        return text is "-0" ? "0" : text;
    }

    /// <summary>
    /// The colour in a notation: <c>#rrggbb</c> (<c>#rrggbbaa</c> below full opacity), <c>rgb(255 136 0)</c>, <c>hsl(32 100% 50%)</c> or <c>oklch(0.74354 0.17743 57.12)</c>, each with
    /// <c> / alpha</c> below full opacity. The oklch value has five decimals for lightness and chroma and two for the hue; fewer would not give back the same eight-bit colour for dark channels, where sRGB is steep.
    /// </summary>
    public static string Format(Rgba color, ColorFormat format, bool upperHex = false)
    {
        string alpha = color.A < 1 ? " / " + Num(color.A, 3) : "";
        switch (format)
        {
            case ColorFormat.Hex:
                string hex = $"#{color.R:x2}{color.G:x2}{color.B:x2}" + (color.A < 1 ? $"{(int)Math.Round(color.A * 255, MidpointRounding.AwayFromZero):x2}" : "");
                return upperHex ? hex.ToUpperInvariant() : hex;
            case ColorFormat.Rgb:
                return $"rgb({color.R} {color.G} {color.B}{alpha})";
            case ColorFormat.Hsl:
                var (h, s, l) = ToHsl(color);
                return $"hsl({Num(h, 2)} {Num(s * 100, 2)}% {Num(l * 100, 2)}%{alpha})";
            default:
                var (lightness, chroma, hue) = ToOklch(color);
                return $"oklch({Num(lightness, 5)} {Num(chroma, 5)} {Num(hue, 2)}{alpha})";
        }
    }

    /// <summary>The colour in the notation the match was written in; the picker uses it so a new value looks like the old one.</summary>
    public static string FormatLike(ColorMatch match, Rgba color) => Format(color, match.Format, match.UpperHex);
}

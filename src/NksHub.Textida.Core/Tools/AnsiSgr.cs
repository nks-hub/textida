namespace NksHub.Textida.Core.Tools;

/// <summary>A terminal color: the default one, an entry of the 256-color table (0-15 are the standard and bright colors) or a true color.</summary>
public readonly record struct AnsiColor(AnsiColorKind Kind, int Value)
{
    public static AnsiColor Default => default;
    public static AnsiColor Indexed(int index) => new(AnsiColorKind.Indexed, index);
    public static AnsiColor Rgb(int r, int g, int b) => new(AnsiColorKind.Rgb, r << 16 | g << 8 | b);

    public bool IsDefault => Kind == AnsiColorKind.Default;

    private static readonly int[] Standard =
    [
        0x000000, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
        0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xFFFFFF,
    ];

    /// <summary>The color as 0xRRGGBB; the default color has none.</summary>
    public int? ToRgb() => Kind switch
    {
        AnsiColorKind.Rgb => Value,
        AnsiColorKind.Indexed when Value < 16 => Standard[Value],
        AnsiColorKind.Indexed when Value < 232 => Cube(Value - 16),
        AnsiColorKind.Indexed => Gray(Value - 232),
        _ => null,
    };

    private static int Cube(int n)
    {
        static int Level(int i) => i == 0 ? 0 : 55 + 40 * i;
        return Level(n / 36) << 16 | Level(n / 6 % 6) << 8 | Level(n % 6);
    }

    private static int Gray(int n)
    {
        int v = 8 + 10 * n;
        return v << 16 | v << 8 | v;
    }
}

public enum AnsiColorKind : byte { Default, Indexed, Rgb }

/// <summary>What the SGR codes in force say about the next characters.</summary>
public readonly record struct AnsiStyle(AnsiColor Fore, AnsiColor Back, bool Bold, bool Underline)
{
    public bool IsDefault => this == default;
}

/// <summary>A run of visible characters in one non-default style; <paramref name="Start"/> is an index into the line.</summary>
public readonly record struct AnsiSpan(int Start, int Length, AnsiStyle Style);

/// <summary>Result of reading one line: where the escape sequences are (to hide them), the styled runs and the style in force at the end.</summary>
public sealed record AnsiLine(IReadOnlyList<(int Start, int Length)> Escapes, IReadOnlyList<AnsiSpan> Spans, AnsiStyle End)
{
    public static AnsiLine None { get; } = new([], [], default);
}

/// <summary>
/// Reads ANSI escape sequences (CSI ... final byte) in a line of text. Every complete sequence is reported as hidden; those ending in <c>m</c> change the style
/// (SGR: reset, bold, underline, 16, 256 and true colors). A sequence that is cut off or has a byte that cannot belong to it is not an escape: it stays in
/// the text as it is and reading goes on right after its ESC, so no text is ever lost. The text is never changed.
/// </summary>
public static class AnsiSgr
{
    private const char Esc = '\x1b';
    /// <summary>Longest sequence taken as one; anything longer is not an escape.</summary>
    private const int MaxSequence = 64;

    public static bool Contains(ReadOnlySpan<char> line) => line.Contains(Esc);

    /// <summary>Reads the line; <paramref name="start"/> is the style in force at its beginning.</summary>
    public static AnsiLine Parse(ReadOnlySpan<char> line, AnsiStyle start = default)
    {
        if (!Contains(line)) return start.IsDefault || line.IsEmpty ? new AnsiLine([], [], start) : new AnsiLine([], [new AnsiSpan(0, line.Length, start)], start);
        var escapes = new List<(int, int)>();
        var spans = new List<AnsiSpan>();
        var style = start;
        int runStart = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != Esc || Sequence(line, i) is not { } length) continue;
            AddSpan(spans, runStart, i, style);
            escapes.Add((i, length));
            if (line[i + length - 1] == 'm') style = Apply(style, line.Slice(i + 2, length - 3));
            i += length - 1;
            runStart = i + 1;
        }
        AddSpan(spans, runStart, line.Length, style);
        return new AnsiLine(escapes, spans, style);
    }

    private static void AddSpan(List<AnsiSpan> spans, int from, int to, AnsiStyle style)
    {
        if (to <= from || style.IsDefault) return;
        if (spans.Count > 0 && spans[^1] is var last && last.Start + last.Length == from && last.Style == style) spans[^1] = last with { Length = last.Length + to - from };
        else spans.Add(new AnsiSpan(from, to - from, style));
    }

    /// <summary>Length of the complete CSI sequence that begins at <paramref name="at"/>, or null when there is none.</summary>
    public static int? Sequence(ReadOnlySpan<char> line, int at)
    {
        if (at + 2 >= line.Length || line[at + 1] != '[') return null;
        int i = at + 2;
        while (i < line.Length && i - at < MaxSequence && line[i] is >= '\x30' and <= '\x3F') i++;
        while (i < line.Length && i - at < MaxSequence && line[i] is >= '\x20' and <= '\x2F') i++;
        return i < line.Length && i - at < MaxSequence && line[i] is >= '\x40' and <= '\x7E' ? i - at + 1 : null;
    }

    private static AnsiStyle Apply(AnsiStyle style, ReadOnlySpan<char> parameters)
    {
        foreach (char c in parameters)
            if (c is not (>= '0' and <= '9' or ';')) return style; // private or intermediate bytes: not a color sequence
        var values = new List<int>(8);
        foreach (var range in parameters.Split(';'))
        {
            values.Add(Number(parameters[range]));
        }
        for (int i = 0; i < values.Count; i++)
        {
            int code = values[i];
            switch (code)
            {
                case 0: style = default; break;
                case 1: style = style with { Bold = true }; break;
                case 22: style = style with { Bold = false }; break;
                case 4: style = style with { Underline = true }; break;
                case 24: style = style with { Underline = false }; break;
                case >= 30 and <= 37: style = style with { Fore = AnsiColor.Indexed(code - 30) }; break;
                case >= 90 and <= 97: style = style with { Fore = AnsiColor.Indexed(code - 90 + 8) }; break;
                case 39: style = style with { Fore = default }; break;
                case >= 40 and <= 47: style = style with { Back = AnsiColor.Indexed(code - 40) }; break;
                case >= 100 and <= 107: style = style with { Back = AnsiColor.Indexed(code - 100 + 8) }; break;
                case 49: style = style with { Back = default }; break;
                case 38 or 48:
                    if (Extended(values, ref i) is not { } color) return style;
                    style = code == 38 ? style with { Fore = color } : style with { Back = color };
                    break;
            }
        }
        return style;
    }

    /// <summary>Reads <c>5;n</c> or <c>2;r;g;b</c> after 38 or 48; null (and the rest of the sequence ignored) when it is incomplete or out of range.</summary>
    private static AnsiColor? Extended(List<int> values, ref int i)
    {
        if (i + 1 >= values.Count) return null;
        if (values[i + 1] == 5)
        {
            if (i + 2 >= values.Count || values[i + 2] is < 0 or > 255) return null;
            i += 2;
            return AnsiColor.Indexed(values[i]);
        }
        if (values[i + 1] != 2) return null;
        int at = i + 2;
        if (at + 2 >= values.Count) return null;
        int r = values[at], g = values[at + 1], b = values[at + 2];
        if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255) return null;
        i = at + 2;
        return AnsiColor.Rgb(r, g, b);
    }

    /// <summary>An empty parameter means 0; one that is not a plain number (the colon form, for one) is -1 and is ignored.</summary>
    private static int Number(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return 0;
        return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : -1;
    }
}

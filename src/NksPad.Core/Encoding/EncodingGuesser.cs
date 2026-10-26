using System.Text;
using System.Text.Unicode;

namespace NksPad.Core.Encodings;

/// <param name="Confidence">0..1. Below <see cref="EncodingGuesser.UncertainBelow"/> the UI should ask the user.</param>
/// <param name="Alternatives">Other plausible codecs, most likely first; empty when the result is certain.</param>
public sealed record EncodingGuess(TextCodec Codec, double Confidence, string Reason, IReadOnlyList<TextCodec> Alternatives)
{
    public bool IsUncertain => Confidence < EncodingGuesser.UncertainBelow;
}

/// <summary>
/// Order: BOM, UTF-32 and UTF-16 without BOM (byte pattern), ASCII, strict UTF-8, then a statistical choice
/// among the 8-bit pages. The statistics are letter sets per language (own tables), not frequencies from any other program.
/// </summary>
public static class EncodingGuesser
{
    public const double UncertainBelow = 0.8;

    private static readonly TextCodec[] EightBit = [TextCodec.Cp1250, TextCodec.Cp1252, TextCodec.Latin2, TextCodec.Kamenicky];

    private static readonly char[][] Tables = EightBit.Select(c =>
    {
        var t = new char[256];
        for (int i = 0; i < 256; i++) t[i] = c.Decode(new[] { (byte)i }).Text[0];
        return t;
    }).ToArray();

    private static readonly string[] Languages =
    [
        "áčďéěíňóřšťúůýž",       // Czech
        "áäčďéíĺľňóôŕšťúýž",     // Slovak
        "ąćęłńóśźż",             // Polish
        "áéíóöőúüű",             // Hungarian
        "äöüß",                  // German
        "àâçèéêëîïôùûüÿœæ",      // French
        "áéíñóúü",               // Spanish
        "àèéìíòóù",              // Italian
        "áâãàçéêíóôõú",          // Portuguese
        "åæøäöéü",               // Danish, Norwegian, Swedish
        "äöåšž",                 // Finnish, Estonian
    ];

    private const string PlainPunctuation = " «»„“”‘’–—•…°§©®×÷€±¼½¾²³µ¶·";

    /// <param name="sample">Leading bytes of the file.</param>
    /// <param name="wholeFile">False when the sample may end inside a character.</param>
    /// <param name="defaultCodec">Result for an empty file; UTF-8 without BOM when null.</param>
    public static EncodingGuess Guess(ReadOnlySpan<byte> sample, bool wholeFile, TextCodec? defaultCodec = null)
    {
        if (sample.IsEmpty) return new(defaultCodec ?? TextCodec.Utf8, 1, "empty file, default encoding", []);

        foreach (var c in new[] { TextCodec.Utf8Bom, TextCodec.Utf32LEBom, TextCodec.Utf32BEBom, TextCodec.Utf16LEBom, TextCodec.Utf16BEBom })
            if (sample.StartsWith(c.Bom)) return new(c, 1, "byte order mark", []);

        if (GuessWideWithoutBom(sample) is { } wide) return wide;

        if (Ascii.IsValid(sample)) return new(TextCodec.Utf8, 1, "plain ASCII", []);

        var checkSpan = wholeFile ? sample : sample[..Utf8Validator.CompleteLength(sample)];
        if (Utf8.IsValid(checkSpan))
        {
            int multi = 0;
            foreach (byte b in checkSpan) if (b >= 0xC0) multi++; // lead bytes of a validated stream
            double conf = multi >= 3 ? 0.99 : multi == 2 ? 0.95 : 0.85;
            return new(TextCodec.Utf8, conf, $"valid UTF-8 with {multi} multi-byte character(s)", conf < 0.99 ? [TextCodec.Cp1250, TextCodec.Cp1252] : []);
        }
        return GuessEightBit(sample);
    }

    private static EncodingGuess? GuessWideWithoutBom(ReadOnlySpan<byte> s)
    {
        int n32 = Math.Min(s.Length, 8192) & ~3;
        if (n32 >= 8)
        {
            int le = 0, be = 0, quads = n32 / 4;
            for (int i = 0; i < n32; i += 4)
            {
                if (s[i + 3] == 0 && s[i + 2] <= 0x10) le += s[i + 2] == 0 ? 1 : 0; else le = int.MinValue / 2;
                if (s[i] == 0 && s[i + 1] <= 0x10) be += s[i + 1] == 0 ? 1 : 0; else be = int.MinValue / 2;
            }
            double conf = n32 >= 32 ? 0.95 : 0.6;
            if (le >= quads * 0.9) return new(TextCodec.Utf32LE, conf, "UTF-32 LE byte pattern without BOM", conf < UncertainBelow ? [TextCodec.Utf8] : []);
            if (be >= quads * 0.9) return new(TextCodec.Utf32BE, conf, "UTF-32 BE byte pattern without BOM", conf < UncertainBelow ? [TextCodec.Utf8] : []);
        }

        int n = Math.Min(s.Length, 8192) & ~1;
        if (n < 4) return null;
        int half = n / 2, zeroEven = 0, zeroOdd = 0, lowEven = 0, lowOdd = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (s[i] == 0) zeroEven++;
            if (s[i + 1] == 0) zeroOdd++;
            if (s[i] < 0x20) lowEven++;
            if (s[i + 1] < 0x20) lowOdd++;
        }
        double c16 = n >= 32 ? 0.95 : 0.6;
        if (zeroOdd > half * 0.3 && zeroEven < half * 0.05) return Wide16(TextCodec.Utf16LE, c16, "Latin text with a zero high byte");
        if (zeroEven > half * 0.3 && zeroOdd < half * 0.05) return Wide16(TextCodec.Utf16BE, c16, "Latin text with a zero high byte");
        // Scripts such as Cyrillic keep the high byte in 0x01..0x1F; ordinary 8-bit text does not have control bytes in half its positions.
        if (n >= 32 && lowOdd >= half * 0.9 && lowEven < half * 0.1) return Wide16(TextCodec.Utf16LE, 0.8, "high bytes in the control range");
        if (n >= 32 && lowEven >= half * 0.9 && lowOdd < half * 0.1) return Wide16(TextCodec.Utf16BE, 0.8, "high bytes in the control range");
        return null;
    }

    private static EncodingGuess Wide16(TextCodec codec, double conf, string why) =>
        new(codec, conf, $"UTF-16 byte pattern without BOM ({why})", conf < UncertainBelow ? [TextCodec.Utf8] : []);

    private static EncodingGuess GuessEightBit(ReadOnlySpan<byte> sample)
    {
        var high = new List<byte>();
        foreach (byte b in sample[..Math.Min(sample.Length, 1 << 20)]) if (b >= 0x80) high.Add(b);
        var scores = EightBit.Select(c => (Codec: c, Score: Score(Tables[Array.IndexOf(EightBit, c)], high))).ToList();
        var ordered = scores.OrderByDescending(x => x.Score).ToList(); // stable: ties keep the EightBit order
        double s1 = ordered[0].Score, margin = s1 - ordered[1].Score;
        double conf = s1 <= 0 ? 0.2 : Math.Min(0.95, 0.5 + 1.5 * margin) * Math.Min(1, high.Count / 4.0) * Math.Min(1, s1);
        var alternatives = conf < UncertainBelow
            ? ordered.Skip(1).Where(x => x.Score >= s1 * 0.5).Select(x => x.Codec).ToList()
            : [];
        return new(ordered[0].Codec, conf, $"not valid UTF-8; {ordered[0].Codec.Name} fits {s1:P0} of {high.Count} high bytes", alternatives);
    }

    /// <summary>Share of high bytes that decode to letters of one language (foreign letters subtract), minus penalties for controls and stray symbols.</summary>
    private static double Score(char[] table, List<byte> high)
    {
        if (high.Count == 0) return 0;
        var perLang = new int[Languages.Length];
        double penalty = 0;
        foreach (byte b in high)
        {
            char c = table[b];
            if (char.IsControl(c)) { penalty += 1; continue; }
            if (char.IsLetter(c))
            {
                char lower = char.ToLowerInvariant(c);
                for (int l = 0; l < Languages.Length; l++) perLang[l] += Languages[l].Contains(lower) ? 1 : -1; // a foreign letter counts against the language
            }
            else if (!PlainPunctuation.Contains(c)) penalty += 0.5;
        }
        return Math.Max(0, (perLang.Max() - penalty) / high.Count);
    }
}

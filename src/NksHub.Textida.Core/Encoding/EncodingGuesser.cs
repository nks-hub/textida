using System.Text;
using System.Text.Unicode;

namespace NksHub.Textida.Core.Encodings;

/// <param name="Confidence">0..1. Below <see cref="EncodingGuesser.UncertainBelow"/> the UI should ask the user.</param>
/// <param name="Alternatives">Other plausible codecs, most likely first; empty when the result is certain.</param>
public sealed record EncodingGuess(TextCodec Codec, double Confidence, string Reason, IReadOnlyList<TextCodec> Alternatives)
{
    public bool IsUncertain => Confidence < EncodingGuesser.UncertainBelow;
}

/// <summary>
/// Order: BOM, UTF-32 and UTF-16 without BOM (byte pattern), ASCII, strict UTF-8, then a statistical choice
/// among the 8-bit pages. The statistics are letter sets per language (own tables), not frequencies from any other program; for the Cyrillic pages, which decode the same bytes to letters of
/// every case, the lowercase letters are weighed by how common they are in Russian, because text is mostly lowercase and only the right page turns it into lowercase.
/// Greek and Turkish text decodes to the same letters in the Windows and the ISO page, so there the result is uncertain and offers both.
/// </summary>
public static class EncodingGuesser
{
    public const double UncertainBelow = 0.8;

    private static readonly TextCodec[] EightBit =
    [
        TextCodec.Cp1250, TextCodec.Cp1252, TextCodec.Latin2, TextCodec.Kamenicky,
        TextCodec.ByName("Windows-1251")!, TextCodec.ByName("KOI8-R")!, TextCodec.ByName("DOS CP866")!,
        TextCodec.ByName("Windows-1253")!, TextCodec.ByName("ISO-8859-7")!, TextCodec.ByName("Windows-1254")!, TextCodec.ByName("ISO-8859-9")!,
    ];

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
        "çğışöü",                // Turkish
    ];

    /// <summary>The lowercase Cyrillic letters by how common they are in Russian text (and the few of Ukrainian), most common first.</summary>
    private const string CyrillicByFrequency = "оеаинтсрвлкмдпуяыьгзбчйхжшюцщэфъёіїє";

    /// <summary>The lowercase Greek letters by how common they are in Greek text, most common first (accented vowels and the final sigma count with their plain letters).</summary>
    private const string GreekByFrequency = "αοτιενρσςκηυμπλωδγχθφβξζψάέήίόύώ";

    private static double GreekWeight(char c)
    {
        if (char.IsUpper(c)) return 0.15;
        int rank = GreekByFrequency.IndexOf(c);
        return rank < 0 ? 0.2 : rank < 9 ? 1.0 : rank < 17 ? 0.8 : rank < 25 ? 0.4 : 0.7;
    }

    private static double CyrillicWeight(char c)
    {
        if (char.IsUpper(c)) return 0.15; // capitals are rare inside text, so a page that makes capitals of most bytes is the wrong page
        int rank = CyrillicByFrequency.IndexOf(c);
        return rank < 0 ? 0.2 : rank < 10 ? 1.0 : rank < 20 ? 0.8 : rank < 28 ? 0.5 : 0.2;
    }

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
        var eightBit = GuessEightBit(sample);
        return GuessMultiByte(sample, wholeFile) is { } asian && asian.Confidence > eightBit.Confidence ? asian : eightBit;
    }

    // ---- Shift-JIS, EUC-JP, GBK, Big5, EUC-KR ----

    private static readonly TextCodec[] MultiByte = [.. new[] { "Shift-JIS", "EUC-JP", "GBK", "Big5", "EUC-KR" }.Select(n => TextCodec.ByName(n)!)];

    /// <summary>Characters that only simplified Chinese writes (the simplified form of a pair that differs) and that only traditional Chinese writes; the other form of the pair is the proof of the other script.</summary>
    private static readonly HashSet<char> SimplifiedOnly = [.. "这个们时来国对会发动从学后经进过种产当点开关无机实现体业应与义样议电长军党变条结间内数单风场车务万达积声报斗类离华确张节话历劳团办响约习为"];
    private static readonly HashSet<char> TraditionalOnly = [.. "這個們時來國對會發動從學後經進過種產當點開關無機實現體業應與義樣議電長軍黨變條結間內數單風場車務萬達積聲報鬥類離華確張節話歷勞團辦響約習為"];

    /// <summary>
    /// The Asian multi-byte encoding the bytes read as, or null. Only an encoding that reads every byte as a character (no invalid sequence) counts; then the characters decide: kana mean Japanese,
    /// Hangul means Korean, and Han characters are told apart by the forms that only simplified or only traditional writing uses. The share of such characters in the text is the confidence.
    /// </summary>
    private static EncodingGuess? GuessMultiByte(ReadOnlySpan<byte> sample, bool wholeFile)
    {
        var head = sample[..Math.Min(sample.Length, 1 << 16)];
        bool cut = !wholeFile || head.Length < sample.Length;
        int high = 0;
        foreach (byte b in head) if (b >= 0x80) high++;
        if (high < 4) return null;
        var scored = new List<(TextCodec Codec, double Score)>();
        foreach (var codec in MultiByte)
        {
            var bytes = cut ? head[..codec.CompleteLength(head)] : head;
            var decoded = codec.Decode(bytes.ToArray());
            if (decoded.InvalidCount > 0) continue;
            int wide = 0, kana = 0, halfKana = 0, hangul = 0, han = 0, simplified = 0, traditional = 0;
            foreach (char c in decoded.Text)
            {
                if (c < 0x80) continue;
                wide++;
                if (c is >= '\u3040' and <= '\u30FF') kana++;
                else if (c is >= '\uFF61' and <= '\uFF9F') halfKana++; // single bytes of Shift-JIS, and what other encodings' bytes turn into by chance: a weak sign
                else if (c is >= '\uAC00' and <= '\uD7A3') hangul++;
                else if (c is >= '\u4E00' and <= '\u9FFF')
                {
                    han++;
                    if (SimplifiedOnly.Contains(c)) simplified++;
                    else if (TraditionalOnly.Contains(c)) traditional++;
                }
            }
            if (wide == 0) continue;
            // The proof of the script, in the share of the characters: real Japanese is about half kana, Korean is Hangul, and Chinese has forms that only its own script writes (random bytes read as Han
            // characters almost never hit those few). Without the proof the encoding is only a guess that the bytes happen to be pairs, and it is scored low.
            double score = codec.Name switch
            {
                "Shift-JIS" or "EUC-JP" => kana >= wide * 0.1 && hangul == 0 ? (kana + halfKana * 0.2 + han * 0.5) / wide : 0.1,
                "EUC-KR" => hangul >= wide * 0.5 ? (hangul + han * 0.5) / wide : 0.1,
                "GBK" => kana == 0 && hangul == 0 && simplified > traditional && simplified >= wide * 0.04 ? Math.Min(1, (han * 0.55 + simplified * 2.0) / wide) : 0.1,
                _ => kana == 0 && hangul == 0 && traditional > simplified && traditional >= wide * 0.04 ? Math.Min(1, (han * 0.55 + traditional * 2.0) / wide) : 0.1, // Big5
            };
            scored.Add((codec, Math.Min(1, score)));
        }
        if (scored.Count == 0) return null;
        var ordered = scored.OrderByDescending(x => x.Score).ToList();
        double best = ordered[0].Score, margin = best - (ordered.Count > 1 ? ordered[1].Score : 0);
        if (best < 0.45) return null; // a text of some other script that happens to read as pairs of bytes
        double conf = Math.Min(0.95, 0.5 + margin) * Math.Min(1, high / 8.0);
        var alternatives = conf < UncertainBelow ? ordered.Skip(1).Where(x => x.Score >= best * 0.5).Select(x => x.Codec).ToList() : [];
        return new(ordered[0].Codec, conf, $"not valid UTF-8; reads as {ordered[0].Codec.Name} with {best:P0} of its characters typical", alternatives);
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
        var perLang = new double[Languages.Length];
        double penalty = 0, cyrillic = 0, greek = 0;
        foreach (byte b in high)
        {
            char c = table[b];
            if (char.IsControl(c)) { penalty += 1; continue; }
            if (char.IsLetter(c) && c is >= '\u0400' and <= '\u04FF')
            {
                cyrillic += CyrillicWeight(c);
                for (int l = 0; l < Languages.Length; l++) perLang[l] -= 1;
            }
            else if (char.IsLetter(c) && c is >= '\u0370' and <= '\u03FF')
            {
                greek += GreekWeight(c);
                for (int l = 0; l < Languages.Length; l++) perLang[l] -= 1;
            }
            else if (char.IsLetter(c))
            {
                char lower = char.ToLowerInvariant(c);
                for (int l = 0; l < Languages.Length; l++) perLang[l] += Languages[l].Contains(lower) ? 1 : -1; // a foreign letter counts against the language
            }
            else if (!PlainPunctuation.Contains(c)) penalty += 0.5;
        }
        return Math.Max(0, (Math.Max(perLang.Max(), Math.Max(cyrillic, greek)) - penalty) / high.Count);
    }
}

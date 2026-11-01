using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace NksHub.Textida.Core.Data;

/// <summary>What the editor can say about one character: its code point, its Unicode name, its general category and the bytes it takes in UTF-8 and UTF-16.</summary>
public sealed record CharacterInfo(int CodePoint, string? Name, UnicodeCategory Category, byte[] Utf8, byte[] Utf16)
{
    private static readonly Lazy<(Dictionary<int, string> Names, List<(int From, int To, string Prefix)> Ranges)> Table = new(Load);

    private static readonly string[] C0 =
    [
        "NULL", "START OF HEADING", "START OF TEXT", "END OF TEXT", "END OF TRANSMISSION", "ENQUIRY", "ACKNOWLEDGE", "BELL", "BACKSPACE", "CHARACTER TABULATION", "LINE FEED", "LINE TABULATION", "FORM FEED", "CARRIAGE RETURN",
        "SHIFT OUT", "SHIFT IN", "DATA LINK ESCAPE", "DEVICE CONTROL ONE", "DEVICE CONTROL TWO", "DEVICE CONTROL THREE", "DEVICE CONTROL FOUR", "NEGATIVE ACKNOWLEDGE", "SYNCHRONOUS IDLE", "END OF TRANSMISSION BLOCK",
        "CANCEL", "END OF MEDIUM", "SUBSTITUTE", "ESCAPE", "INFORMATION SEPARATOR FOUR", "INFORMATION SEPARATOR THREE", "INFORMATION SEPARATOR TWO", "INFORMATION SEPARATOR ONE",
    ];

    private static readonly string[] HangulL = ["G", "GG", "N", "D", "DD", "R", "M", "B", "BB", "S", "SS", "", "J", "JJ", "C", "K", "T", "P", "H"];
    private static readonly string[] HangulV = ["A", "AE", "YA", "YAE", "EO", "E", "YEO", "YE", "O", "WA", "WAE", "OE", "YO", "U", "WEO", "WE", "WI", "YU", "EU", "YI", "I"];
    private static readonly string[] HangulT = ["", "G", "GG", "GS", "N", "NJ", "NH", "D", "L", "LG", "LM", "LB", "LS", "LT", "LP", "LH", "M", "B", "BS", "S", "SS", "NG", "J", "C", "K", "T", "P", "H"];

    private static (Dictionary<int, string>, List<(int, int, string)>) Load()
    {
        var names = new Dictionary<int, string>(34000);
        var ranges = new List<(int, int, string)>();
        using var raw = typeof(CharacterInfo).Assembly.GetManifestResourceStream("UnicodeNames.gz") ?? throw new InvalidOperationException("The table of Unicode names is missing.");
        using var gz = new GZipStream(raw, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        for (string? line; (line = reader.ReadLine()) is not null;)
        {
            int space = line.IndexOf(' ');
            if (space < 0) continue;
            if (line[0] == 'R')
            {
                int dash = line.IndexOf('-');
                ranges.Add((int.Parse(line.AsSpan(1, dash - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture), int.Parse(line.AsSpan(dash + 1, space - dash - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture), line[(space + 1)..]));
            }
            else names[int.Parse(line.AsSpan(0, space), NumberStyles.HexNumber, CultureInfo.InvariantCulture)] = line[(space + 1)..];
        }
        return (names, ranges);
    }

    /// <summary>The Unicode name of a code point (the standard name, or the one of the control code); null for a code point that has none (unassigned, private use, surrogate).</summary>
    public static string? NameOf(int codePoint)
    {
        if (codePoint is >= 0 and < 32) return C0[codePoint];
        if (codePoint == 0x7F) return "DELETE";
        if (codePoint is >= 0x80 and < 0xA0) return null; // C1 controls have no name in the data file, only aliases
        if (codePoint is >= 0xAC00 and <= 0xD7A3)
        {
            int s = codePoint - 0xAC00;
            return "HANGUL SYLLABLE " + HangulL[s / 588] + HangulV[s % 588 / 28] + HangulT[s % 28];
        }
        var (names, ranges) = Table.Value;
        if (names.TryGetValue(codePoint, out var name)) return name;
        foreach (var (from, to, prefix) in ranges)
            if (codePoint >= from && codePoint <= to) return prefix + "-" + codePoint.ToString("X4", CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>The facts about the character that starts <paramref name="text"/> at <paramref name="index"/> (a pair of surrogates is one character); null at the end of the text.</summary>
    public static CharacterInfo? At(string text, int index)
    {
        if (index < 0 || index >= text.Length) return null;
        int length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
        string piece = text.Substring(index, length);
        int cp = length == 2 ? char.ConvertToUtf32(piece[0], piece[1]) : piece[0];
        var utf16 = new byte[piece.Length * 2];
        Encoding.BigEndianUnicode.GetBytes(piece, utf16);
        byte[] utf8 = length == 1 && char.IsSurrogate(piece[0]) ? [] : Encoding.UTF8.GetBytes(piece); // a lone surrogate has no UTF-8
        return new CharacterInfo(cp, NameOf(cp), length == 2 ? CharUnicodeInfo.GetUnicodeCategory(cp) : CharUnicodeInfo.GetUnicodeCategory(piece[0]), utf8, utf16);
    }

    /// <summary>The two-letter general category (Lu, Ll, Nd, Zs, ...) of the character.</summary>
    public string CategoryCode => Category switch
    {
        UnicodeCategory.UppercaseLetter => "Lu", UnicodeCategory.LowercaseLetter => "Ll", UnicodeCategory.TitlecaseLetter => "Lt", UnicodeCategory.ModifierLetter => "Lm", UnicodeCategory.OtherLetter => "Lo",
        UnicodeCategory.NonSpacingMark => "Mn", UnicodeCategory.SpacingCombiningMark => "Mc", UnicodeCategory.EnclosingMark => "Me",
        UnicodeCategory.DecimalDigitNumber => "Nd", UnicodeCategory.LetterNumber => "Nl", UnicodeCategory.OtherNumber => "No",
        UnicodeCategory.SpaceSeparator => "Zs", UnicodeCategory.LineSeparator => "Zl", UnicodeCategory.ParagraphSeparator => "Zp",
        UnicodeCategory.Control => "Cc", UnicodeCategory.Format => "Cf", UnicodeCategory.Surrogate => "Cs", UnicodeCategory.PrivateUse => "Co", UnicodeCategory.OtherNotAssigned => "Cn",
        UnicodeCategory.ConnectorPunctuation => "Pc", UnicodeCategory.DashPunctuation => "Pd", UnicodeCategory.OpenPunctuation => "Ps", UnicodeCategory.ClosePunctuation => "Pe",
        UnicodeCategory.InitialQuotePunctuation => "Pi", UnicodeCategory.FinalQuotePunctuation => "Pf", UnicodeCategory.OtherPunctuation => "Po",
        UnicodeCategory.MathSymbol => "Sm", UnicodeCategory.CurrencySymbol => "Sc", UnicodeCategory.ModifierSymbol => "Sk", UnicodeCategory.OtherSymbol => "So",
        _ => "Cn",
    };

    /// <summary><c>U+00E9</c>, with at least four digits.</summary>
    public string CodePointText => "U+" + CodePoint.ToString("X4", CultureInfo.InvariantCulture);

    public static string Hex(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}

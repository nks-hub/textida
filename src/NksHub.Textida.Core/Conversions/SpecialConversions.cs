using System.Globalization;
using System.Text;

namespace NksHub.Textida.Core.Conversions;

/// <summary>Where the converted text sits: the tab size of the document and the visual column (from 0) at which the text starts in its line.</summary>
public readonly record struct ConversionContext(int TabSize, int Column);

/// <summary>
/// One text conversion of the editor: a plain function from the text of a selection to the new text, or null when the text is not in the form the conversion
/// reads. <paramref name="TitleKey"/> is the key of its name in the command texts of the workbench; <paramref name="Sample"/> is previewed when nothing is selected.
/// </summary>
public sealed record SpecialConversion(string Id, string TitleKey, Func<string, ConversionContext, string?> Convert, string? Sample = null)
{
    public const string CommandPrefix = "nks.edit.convert.";

    /// <summary>The command that runs this conversion over every selection at once, without the list.</summary>
    public string CommandId => CommandPrefix + Id;
}

/// <summary>
/// The closed list of special conversions (F048). Each id has its command, its row in the test that closes the list and its samples there; the editor offers
/// the list next to the converters of data plugins, over every selection as one undo step. Bytes behind URL, quoted-printable and Base64 are UTF-8.
/// </summary>
public static class SpecialConversions
{
    // Placeholder, OPEN: the ids follow F048 and wait for the owner's approval as the input decision of P4.2.2; Add Accent Marks needs the dictionary of F079.
    public static IReadOnlyList<SpecialConversion> All { get; } =
    [
        new("decToHex", "cmd.edit.convert.decToHex", (s, _) => OneValue(s, DecimalToHex), "255"),
        new("hexToDec", "cmd.edit.convert.hexToDec", (s, _) => OneValue(s, HexToDecimal), "FF"),
        new("urlEncode", "cmd.edit.convert.urlEncode", (s, _) => Uri.EscapeDataString(s), "a b/č"),
        new("urlDecode", "cmd.edit.convert.urlDecode", (s, _) => Uri.UnescapeDataString(s), "a%20b%2F%C4%8D"),
        new("quotedPrintableEncode", "cmd.edit.convert.quotedPrintableEncode", (s, _) => QuotedPrintableEncode(s), "č = 1"),
        new("quotedPrintableDecode", "cmd.edit.convert.quotedPrintableDecode", (s, _) => QuotedPrintableDecode(s), "=C4=8D =3D 1"),
        new("base64Encode", "cmd.edit.convert.base64Encode", (s, _) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)), "Ahoj"),
        new("base64Decode", "cmd.edit.convert.base64Decode", (s, _) => OneValue(s, Base64Decode), "QWhvag=="),
        new("removeAccents", "cmd.edit.convert.removeAccents", (s, _) => RemoveAccents(s), "Příliš žluťoučký kůň"),
        new("tabsToSpaces", "cmd.edit.convert.tabsToSpaces", TabsToSpaces, "a\tb"),
    ];

    /// <summary>Converts the one value the text holds; the white space around it stays. Null when there is no value or it is not readable.</summary>
    private static string? OneValue(string s, Func<string, string?> convert)
    {
        int start = 0, end = s.Length;
        while (start < end && char.IsWhiteSpace(s[start])) start++;
        while (end > start && char.IsWhiteSpace(s[end - 1])) end--;
        if (start == end || convert(s[start..end]) is not { } converted) return null;
        return string.Concat(s.AsSpan(0, start), converted, s.AsSpan(end));
    }

    private static string? DecimalToHex(string s) =>
        ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n.ToString("X", CultureInfo.InvariantCulture) : null;

    private static string? HexToDecimal(string s)
    {
        var digits = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        return ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static string? Base64Decode(string s)
    {
        var bytes = new byte[s.Length / 4 * 3 + 3];
        return Convert.TryFromBase64String(s, bytes, out int length) ? Utf8Text(bytes.AsSpan(0, length)) : null;
    }

    /// <summary>Printable ASCII and spaces stay, every other byte (line ends too) becomes =XX; there are no soft line breaks.</summary>
    private static string QuotedPrintableEncode(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(s))
            if (b is >= 32 and <= 126 && b != '=') sb.Append((char)b);
            else sb.Append('=').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>=XX becomes the byte, = at a line end (a soft line break) disappears; any other = refuses the text, so a broken one is never half decoded.</summary>
    private static string? QuotedPrintableDecode(string s)
    {
        var bytes = new List<byte>(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            int next = s.IndexOf('=', i);
            if (next < 0) next = s.Length;
            bytes.AddRange(Encoding.UTF8.GetBytes(s, i, next - i));
            i = next;
            if (i == s.Length) break;
            if (i + 2 < s.Length && char.IsAsciiHexDigit(s[i + 1]) && char.IsAsciiHexDigit(s[i + 2]))
            {
                bytes.Add(byte.Parse(s.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 3;
            }
            else if (i + 1 < s.Length && s[i + 1] is '\r' or '\n')
                i += s[i + 1] == '\r' && i + 2 < s.Length && s[i + 2] == '\n' ? 3 : 2;
            else return null;
        }
        return Utf8Text(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes));
    }

    private static string? Utf8Text(ReadOnlySpan<byte> bytes) => System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : null;

    /// <summary>Drops the combining accents of Latin, Greek and Cyrillic letters (č to c); letters without a decomposition (ł, ø, ß) and marks of other scripts stay.</summary>
    private static string RemoveAccents(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (c is < '̀' or > 'ͯ') sb.Append(c);
        return sb.Length == decomposed.Length ? s : sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Every tab becomes the spaces up to the next tab stop; columns count as in the editor, one per character, from the column the text starts at.</summary>
    private static string TabsToSpaces(string s, ConversionContext at)
    {
        if (!s.Contains('\t')) return s;
        int size = Math.Max(1, at.TabSize), column = at.Column;
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            if (c == '\t')
            {
                int spaces = size - column % size;
                sb.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                sb.Append(c);
                column = c is '\r' or '\n' ? 0 : column + 1;
            }
        }
        return sb.ToString();
    }
}

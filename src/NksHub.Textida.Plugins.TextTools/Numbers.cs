using System.Globalization;
using System.Numerics;
using System.Text;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>
/// How numbers are written in one language: the character that separates thousands and the one that starts the fraction.
/// Czech and Slovak group with a no-break space (also a plain space when a whole line is one number), German with a dot, English with a comma.
/// </summary>
public sealed class NumberFormat
{
    private readonly string _group;
    private readonly bool _spaceGroups;

    private NumberFormat(string group, char fraction, bool spaceGroups) => (_group, Fraction, _spaceGroups) = (group, fraction, spaceGroups);

    public char Fraction { get; }

    public static NumberFormat For(string language) => language switch
    {
        "cs" or "sk" => new NumberFormat(" ", ',', true),
        "de" => new NumberFormat(".", ',', false),
        _ => new NumberFormat(",", '.', false),
    };

    private bool IsGroup(char c, bool allowSpace) =>
        _spaceGroups ? c is ' ' or ' ' || (allowSpace && c == ' ') : c == _group[0];

    /// <summary>
    /// Reads a number such as <c>-1 234,50</c> or <c>1.234,5</c>. Groups hold exactly three digits (the first one to three); anything else is not a number.
    /// <paramref name="allowSpace"/> lets a plain space group the digits (Czech, Slovak), which is only safe when the text is one number.
    /// </summary>
    public bool TryParse(ReadOnlySpan<char> text, bool allowSpace, out decimal value)
    {
        value = 0;
        text = text.Trim();
        var sb = new StringBuilder(text.Length);
        int i = 0;
        if (i < text.Length && text[i] is '-' or '+' or '−')
        {
            if (text[i] != '+') sb.Append('-');
            i++;
        }
        int inGroup = 0;
        bool grouped = false, anyDigit = false;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c is >= '0' and <= '9') { sb.Append(c); inGroup++; anyDigit = true; }
            else if (IsGroup(c, allowSpace))
            {
                if (inGroup == 0 || (grouped ? inGroup != 3 : inGroup > 3)) return false;
                grouped = true;
                inGroup = 0;
            }
            else break;
        }
        if (grouped && inGroup != 3) return false;
        if (i < text.Length && text[i] == Fraction)
        {
            i++;
            sb.Append('.');
            int fraction = 0;
            for (; i < text.Length && text[i] is >= '0' and <= '9'; i++, fraction++) sb.Append(text[i]);
            if (fraction == 0) return false;
            anyDigit = true;
        }
        return anyDigit && i == text.Length
            && decimal.TryParse(sb.ToString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public string Format(decimal value) => Localize(value.ToString("0.############################", CultureInfo.InvariantCulture));

    public string Format(double value) => Localize(value.ToString("R", CultureInfo.InvariantCulture));

    public string Format(long value) => Localize(value.ToString(CultureInfo.InvariantCulture));

    private string Localize(string invariant)
    {
        if (invariant.Contains('E')) return invariant.Replace('.', Fraction);
        int dot = invariant.IndexOf('.');
        var whole = dot < 0 ? invariant : invariant[..dot];
        var sign = whole.StartsWith('-') ? "-" : "";
        var digits = whole.TrimStart('-');
        var sb = new StringBuilder(sign);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(_group);
            sb.Append(digits[i]);
        }
        if (dot >= 0) sb.Append(Fraction).Append(invariant, dot + 1, invariant.Length - dot - 1);
        return sb.ToString();
    }
}

/// <param name="Whole">Lines that were one number as a whole; how the format of a block is told apart (see <see cref="BlockCalculator.CalculateAuto"/>).</param>
public sealed record BlockResult(int Count, int Ignored, decimal Sum, decimal Min, decimal Max, decimal Average, int Whole);

/// <summary>
/// Sum, minimum, maximum, average and count of the numbers in some text. Each line is one number when it reads as one; otherwise the line is split at
/// white space and semicolons and every piece that is not a number is skipped and counted in <see cref="BlockResult.Ignored"/> (placeholder behaviour).
/// A sum that does not fit a decimal throws <see cref="OverflowException"/>.
/// </summary>
public static class BlockCalculator
{
    private static readonly char[] Separators = [' ', '\t', ' ', ' ', ';'];

    public static BlockResult Calculate(IEnumerable<string> texts, NumberFormat format)
    {
        int count = 0, ignored = 0, whole = 0;
        decimal sum = 0, min = 0, max = 0;
        void Take(decimal v)
        {
            sum = checked(sum + v);
            min = count == 0 ? v : Math.Min(min, v);
            max = count == 0 ? v : Math.Max(max, v);
            count++;
        }
        foreach (var text in texts)
            foreach (var line in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
            {
                if (line.AsSpan().Trim().IsEmpty) continue;
                if (format.TryParse(line, allowSpace: true, out var single)) { Take(single); whole++; continue; }
                foreach (var piece in line.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (format.TryParse(piece, allowSpace: false, out var v)) Take(v);
                    else ignored++;
                }
            }
        return new BlockResult(count, ignored, sum, min, max, count == 0 ? 0 : sum / count, whole);
    }

    /// <summary>
    /// Calculates with the format (Czech/Slovak, German or English) that reads the most lines as one number each, then the most numbers. When several formats
    /// read alike (plain integers, <c>1,234</c>) the one of <paramref name="preferredLanguage"/> wins, then Czech, German, English. A sum that overflows in every format is an overflow.
    /// </summary>
    public static (BlockResult Result, NumberFormat Format) CalculateAuto(IReadOnlyList<string> texts, string preferredLanguage)
    {
        (BlockResult Result, NumberFormat Format)? best = null;
        bool overflow = false;
        foreach (var language in new[] { preferredLanguage, "cs", "de", "en" }.Select(l => l == "sk" ? "cs" : l).Distinct())
        {
            var format = NumberFormat.For(language);
            try
            {
                var result = Calculate(texts, format);
                if (best is null || (result.Whole, result.Count).CompareTo((best.Value.Result.Whole, best.Value.Result.Count)) > 0) best = (result, format);
            }
            catch (OverflowException) { overflow = true; }
        }
        return best ?? (overflow ? throw new OverflowException() : throw new InvalidOperationException());
    }
}

/// <summary>Conversion between the radixes 2 to 16 for the numbers from the lowest signed to the highest unsigned 64-bit value (placeholder range).</summary>
public static class BaseConverter
{
    public static readonly BigInteger Lowest = long.MinValue;
    public static readonly BigInteger Highest = ulong.MaxValue;

    /// <summary>Reads <c>0x</c>, <c>0b</c> and <c>0o</c> prefixed numbers and plain decimal ones, with an optional sign.</summary>
    public static bool TryParse(string text, out BigInteger value)
    {
        value = 0;
        var s = text.Trim();
        bool negative = s.StartsWith('-');
        if (negative || s.StartsWith('+')) s = s[1..];
        int radix = 10;
        if (s.Length > 2 && s[0] == '0')
        {
            radix = char.ToLowerInvariant(s[1]) switch { 'x' => 16, 'b' => 2, 'o' => 8, _ => 10 };
            if (radix != 10) s = s[2..];
        }
        if (s.Length == 0) return false;
        BigInteger result = 0;
        foreach (char c in s)
        {
            int digit = c switch { >= '0' and <= '9' => c - '0', >= 'a' and <= 'f' => c - 'a' + 10, >= 'A' and <= 'F' => c - 'A' + 10, _ => -1 };
            if (digit < 0 || digit >= radix) return false;
            result = result * radix + digit;
            if (result > Highest + 1) return false;
        }
        value = negative ? -result : result;
        return value >= Lowest && value <= Highest;
    }

    private static readonly (int Value, string Symbol)[] RomanSymbols =
        [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];

    /// <summary>Roman numeral of 1 to 3999 in the usual subtractive form; null outside that range.</summary>
    public static string? ToRoman(BigInteger value)
    {
        if (value < 1 || value > 3999) return null;
        int rest = (int)value;
        var sb = new StringBuilder();
        foreach (var (v, symbol) in RomanSymbols)
            for (; rest >= v; rest -= v) sb.Append(symbol);
        return sb.ToString();
    }

    /// <summary>Reads a Roman numeral; only the canonical spelling is accepted (IIII and VX are not numerals).</summary>
    public static bool TryParseRoman(string text, out BigInteger value)
    {
        value = 0;
        var s = text.Trim().ToUpperInvariant();
        if (s.Length == 0 || s.Any(c => "IVXLCDM".IndexOf(c) < 0)) return false;
        int total = 0, i = 0;
        foreach (var (v, symbol) in RomanSymbols)
            while (string.CompareOrdinal(s, i, symbol, 0, symbol.Length) == 0 && i + symbol.Length <= s.Length) { total += v; i += symbol.Length; }
        if (i != s.Length || ToRoman(total) != s) return false;
        value = total;
        return true;
    }

    public static string ToRadix(BigInteger value, int radix)
    {
        if (radix is < 2 or > 16) throw new ArgumentOutOfRangeException(nameof(radix));
        if (value.IsZero) return "0";
        bool negative = value.Sign < 0;
        var rest = BigInteger.Abs(value);
        var sb = new StringBuilder();
        while (!rest.IsZero)
        {
            rest = BigInteger.DivRem(rest, radix, out var digit);
            sb.Insert(0, "0123456789ABCDEF"[(int)digit]);
        }
        return negative ? "-" + sb : sb.ToString();
    }
}

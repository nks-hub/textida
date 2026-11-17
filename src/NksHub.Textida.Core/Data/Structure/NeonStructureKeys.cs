using System.Globalization;
using System.Numerics;
using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

internal static class NeonStructureKeys
{
    internal static long? IntegerKey(StructureCharReader reader, long start, long end)
    {
        long saved = reader.Position;
        try
        {
            using var values = Read(reader, start, end).GetEnumerator();
            if (!values.MoveNext()) return null;
            bool negative = values.Current == '-';
            if (negative && !values.MoveNext()) return null;
            int first = values.Current;
            if (first == '0') return !negative && !values.MoveNext() ? 0 : null;
            if (first is < '1' or > '9') return null;
            ulong result = 0, limit = negative ? 0x8000000000000000UL : long.MaxValue;
            do
            {
                int digit = values.Current - '0';
                if (digit is < 0 or > 9 || result > (limit - (uint)digit) / 10) return null;
                result = result * 10 + (uint)digit;
            } while (values.MoveNext());
            return negative ? result == 0x8000000000000000UL ? long.MinValue : -(long)result : (long)result;
        }
        finally { reader.Seek(saved); }
    }

    internal static IEnumerable<int> Read(StructureCharReader reader, long start, long end)
    {
        reader.Seek(start);
        if (reader.Peek() is '\'' or '"')
        {
            foreach (int c in ConfigScalarText.ReadQuoted(reader, start, end, true)) yield return c;
            yield break;
        }
        if (ConfigScalarText.Classify(reader, start, end, true) != StructureKind.JsonNumber)
        {
            reader.Seek(start);
            while (reader.Position < end) yield return reader.Read();
            yield break;
        }
        reader.Seek(start);
        if (reader.Peek() == '0' && reader.Peek(1) is 'x' or 'o' or 'b')
        {
            reader.Read();
            int radix = reader.Read() switch { 'x' => 16, 'o' => 8, _ => 2 };
            ulong value = 0;
            while (reader.Position < end)
            {
                int digit = ConfigScalarText.Hex(reader.Read());
                if (value > ((ulong)long.MaxValue - (uint)digit) / (uint)radix)
                    throw new StructureSyntaxException(StructureIssueKind.UnsupportedSyntax, start, end,
                        "Radix mapping keys outside the signed 64-bit range are not supported.");
                value = value * (uint)radix + (uint)digit;
            }
            foreach (char c in value.ToString(CultureInfo.InvariantCulture)) yield return c;
            yield break;
        }
        bool integer = true;
        while (reader.Position < end)
        {
            if (reader.Read() is '.' or 'e' or 'E') { integer = false; break; }
        }
        reader.Seek(start);
        if (!integer)
        {
            foreach (char c in FloatKey(reader, end)) yield return c;
            yield break;
        }
        bool negative = reader.Consume('-');
        if (!negative) reader.Consume('+');
        while (reader.Position < end && reader.Peek() == '0') reader.Read();
        if (reader.Position == end) { yield return '0'; yield break; }
        if (negative) yield return '-';
        while (reader.Position < end) yield return reader.Read();
    }

    private static string FloatKey(StructureCharReader reader, long end)
    {
        // 1100 digits cover binary64 midpoint expansions, including subnormals.
        const int retainedDigits = 1100;
        bool negative = reader.Consume('-');
        if (!negative) reader.Consume('+');
        var digits = new StringBuilder(retainedDigits);
        bool fraction = false, nonzero = false, sticky = false;
        long fractionDigits = 0, significantDigits = 0;
        while (reader.Position < end && reader.Peek() is not ('e' or 'E'))
        {
            int c = reader.Read();
            if (c == '.') { fraction = true; continue; }
            if (fraction) fractionDigits++;
            if (!nonzero && c == '0') continue;
            nonzero = true;
            significantDigits++;
            if (digits.Length < retainedDigits) digits.Append((char)c);
            else sticky |= c != '0';
        }
        if (!nonzero) return negative ? "-0" : "0";
        BigInteger exponent = 0;
        if (reader.Position < end)
        {
            reader.Read();
            bool minus = reader.Consume('-');
            if (!minus) reader.Consume('+');
            while (reader.Position < end)
            {
                int c = reader.Read();
                exponent = exponent <= long.MaxValue ? exponent * 10 + c - '0' : (BigInteger)long.MaxValue * 100;
            }
            if (minus) exponent = -exponent;
        }
        exponent += significantDigits - digits.Length;
        exponent -= fractionDigits;
        if (sticky && digits[^1] == '0') digits[^1] = '1';
        double value;
        if (exponent > 400) value = double.PositiveInfinity;
        else if (exponent < -1500) value = 0;
        else
        {
            string number = digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("A parsed NEON floating-point key could not be converted.");
        }
        if (negative) value = -value;
        if (double.IsInfinity(value)) return negative ? "-INF" : "INF";
        if (value == 0) return negative ? "-0" : "0";
        string result = value.ToString("G14", CultureInfo.InvariantCulture);
        int e = result.IndexOf('E');
        if (e < 0) return result;
        string mantissa = result[..e];
        if (!mantissa.Contains('.')) mantissa += ".0";
        int power = int.Parse(result.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return mantissa + "E" + (power >= 0 ? "+" : "-") + Math.Abs(power).ToString(CultureInfo.InvariantCulture);
    }
}

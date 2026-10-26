using System.Buffers.Binary;
using System.Globalization;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

public readonly record struct OffsetContext(long ByteLength, long Cursor, long SelectionStart, uint? Value = null);

public static class OffsetExpression
{
    /// <summary>
    /// Evaluates a byte offset, including EOF. A leading sign makes the expression relative to the cursor.
    /// end is ByteLength; K/M/G are powers of 1024; percentages round down to whole bytes.
    /// Invalid expressions throw FormatException, arithmetic errors throw OverflowException or
    /// DivideByZeroException, and a target outside the document throws ArgumentOutOfRangeException.
    /// </summary>
    public static long Evaluate(string expression, OffsetContext context) =>
        Evaluate(expression, context, null);

    /// <summary>Reads val as uint32 LE from the supplied revision, only when the expression uses it.</summary>
    public static long Evaluate(string expression, DocumentSnapshot snapshot, long cursor, long? selectionStart = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Evaluate(expression, new OffsetContext(snapshot.ByteLength, cursor, selectionStart ?? cursor), snapshot);
    }

    private static long Evaluate(string expression, OffsetContext context, DocumentSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (context.ByteLength < 0 || context.Cursor < 0 || context.Cursor > context.ByteLength ||
            context.SelectionStart < 0 || context.SelectionStart > context.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(context));
        if (expression.Length > 4096) throw new FormatException("The offset expression is too long.");
        var parser = new Parser(expression.AsSpan(), context, snapshot);
        long offset = parser.Parse();
        if (offset < 0 || offset > context.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(expression), "The offset is outside the document.");
        return offset;
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<char> text;
        private readonly OffsetContext context;
        private readonly DocumentSnapshot? snapshot;
        private int position;
        private int depth;

        public Parser(ReadOnlySpan<char> text, OffsetContext context, DocumentSnapshot? snapshot)
        {
            this.text = text;
            this.context = context;
            this.snapshot = snapshot;
        }

        public long Parse()
        {
            SkipSpace();
            bool relative = position < text.Length && text[position] is '+' or '-';
            long result = Sum();
            SkipSpace();
            if (position != text.Length) throw Invalid();
            return relative ? checked(context.Cursor + result) : result;
        }

        private long Sum()
        {
            long result = Product();
            while (true)
            {
                if (Take('+')) result = checked(result + Product());
                else if (Take('-')) result = checked(result - Product());
                else return result;
            }
        }

        private long Product()
        {
            long result = Unary();
            while (true)
            {
                if (Take('*')) result = checked(result * Unary());
                else if (Take('/')) result = checked(result / Unary());
                else return result;
            }
        }

        private long Unary()
        {
            if (++depth > 64) throw new FormatException("The offset expression is nested too deeply.");
            long result;
            if (Take('+')) result = Unary();
            else if (Take('-')) result = checked(-Unary());
            else result = Primary();
            depth--;
            return result;
        }

        private long Primary()
        {
            long result;
            if (Take('('))
            {
                result = Sum();
                if (!Take(')')) throw Invalid();
            }
            else
            {
                SkipSpace();
                int start = position;
                if (position < text.Length && text[position] == '$') position++;
                while (position < text.Length && char.IsAsciiLetterOrDigit(text[position])) position++;
                if (position == start) throw Invalid();
                ReadOnlySpan<char> token = text[start..position];
                if (token.Equals("end", StringComparison.OrdinalIgnoreCase)) result = context.ByteLength;
                else if (token.Equals("sel", StringComparison.OrdinalIgnoreCase)) result = context.SelectionStart;
                else if (token.Equals("val", StringComparison.OrdinalIgnoreCase)) result = ReadValue();
                else result = Number(token);
            }

            if (Take('%'))
            {
                Int128 scaled = (Int128)result * context.ByteLength;
                Int128 whole = scaled / 100;
                if (scaled % 100 < 0) whole--;
                result = checked((long)whole);
            }
            return result;
        }

        private long Number(ReadOnlySpan<char> token)
        {
            bool hex = false;
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
                hex = true;
            }
            else if (token[0] == '$')
            {
                token = token[1..];
                hex = true;
            }
            else if (token[^1] is 'h' or 'H')
            {
                token = token[..^1];
                hex = true;
            }

            long multiplier = 1;
            if (!hex && !token.IsEmpty)
            {
                multiplier = token[^1] switch
                {
                    'k' or 'K' => 1L << 10,
                    'm' or 'M' => 1L << 20,
                    'g' or 'G' => 1L << 30,
                    _ => 1,
                };
                if (multiplier != 1) token = token[..^1];
            }
            if (token.IsEmpty) throw Invalid();
            if (!hex) return checked(long.Parse(token, NumberStyles.None, CultureInfo.InvariantCulture) * multiplier);

            ulong value = ulong.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return checked((long)value);
        }

        private long ReadValue()
        {
            if (snapshot is null)
                return context.Value ?? throw new FormatException("val requires a complete uint32 LE value at the cursor.");
            if (snapshot.ByteLength - context.Cursor < sizeof(uint))
                throw new FormatException("val requires four bytes at the cursor.");
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = snapshot.Read(context.Cursor + read, bytes[read..]);
                if (count <= 0) throw new InvalidOperationException("The snapshot ended before its declared length.");
                read += count;
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        private bool Take(char value)
        {
            SkipSpace();
            if (position == text.Length || text[position] != value) return false;
            position++;
            return true;
        }

        private void SkipSpace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }

        private FormatException Invalid() => new($"Invalid offset expression at character {position + 1}.");
    }
}

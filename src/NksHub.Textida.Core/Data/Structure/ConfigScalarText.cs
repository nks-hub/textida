namespace NksHub.Textida.Core.Data.Structure;

internal static class ConfigScalarText
{
    internal static int Hex(int c) => c is >= '0' and <= '9' ? c - '0'
        : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;

    internal static StructureKind Classify(StructureCharReader reader, long start, long end, bool neon)
    {
        long saved = reader.Position;
        reader.Seek(start);
        Span<char> literal = stackalloc char[16];
        int length = 0, numberState = 0, radix = 0;
        bool number = true, radixDigits = false, mantissaDigits = false;
        while (reader.Position < end)
        {
            int c = reader.Read();
            if (length < literal.Length) literal[length] = c <= char.MaxValue ? (char)c : '\0';
            if (length < 17) length++;
            if (length == 2 && literal[0] == '0' && c is 'x' or 'o' or 'b' && (neon || c != 'b'))
            { radix = c == 'x' ? 16 : c == 'o' ? 8 : 2; number = true; continue; }
            if (radix != 0)
            {
                int digit = Hex(c);
                number &= digit >= 0 && digit < radix;
                radixDigits = true;
                continue;
            }
            if (c is >= '0' and <= '9' && numberState is >= 0 and <= 4) mantissaDigits = true;
            numberState = (numberState, c) switch
            {
                (0, '+' or '-') => 1,
                (0 or 1 or 2, >= '0' and <= '9') => 2,
                (0 or 1 or 2, '.') => 3,
                (3 or 4, >= '0' and <= '9') => 4,
                (2 or 3 or 4, 'e' or 'E') when mantissaDigits => 5,
                (5, '+' or '-') => 6,
                (5 or 6 or 7, >= '0' and <= '9') => 7,
                _ => -1,
            };
            number &= numberState >= 0;
        }
        reader.Seek(saved);
        if (length <= 16)
        {
            var text = literal[..length];
            if (text is "" or "null" or "Null" or "NULL" || !neon && text is "~") return StructureKind.JsonNull;
            if (text is "true" or "True" or "TRUE" or "false" or "False" or "FALSE"
                || neon && text is "yes" or "Yes" or "YES" or "no" or "No" or "NO") return StructureKind.JsonBoolean;
            if (!neon && text is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF"
                or "-.inf" or "-.Inf" or "-.INF" or ".nan" or ".NaN" or ".NAN") return StructureKind.JsonNumber;
        }
        return number && (radix != 0 ? radixDigits : mantissaDigits && numberState is 2 or 3 or 4 or 7)
            ? StructureKind.JsonNumber : StructureKind.JsonString;
    }

    internal static IEnumerable<int> Read(IStructureIndex index, StructureCharReader reader, long start, long end,
        bool name = false, CancellationToken ct = default, long nameStart = -1)
    {
        reader.Seek(start);
        int first = reader.Peek();
        if (first is '\'' or '"') return ReadQuoted(reader, start, end, index.Format == StructureFormat.Neon);
        if (!name && index.Format == StructureFormat.Yaml && first is '|' or '>')
            return ReadBlock(index, reader, start, end, ct, nameStart);
        return ReadPlain(reader, start, end);
    }

    private static IEnumerable<int> ReadPlain(StructureCharReader reader, long start, long end)
    {
        reader.Seek(start);
        long blanks = 0, breaks = 0;
        while (reader.Position < end)
        {
            int c = reader.Read();
            if (c is ' ' or '\t') { blanks++; continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                breaks++;
                blanks = 0;
                continue;
            }
            if (breaks > 0)
            {
                if (breaks == 1) yield return ' ';
                else for (long i = 1; i < breaks; i++) yield return '\n';
            }
            else for (long i = 0; i < blanks; i++) yield return ' ';
            breaks = blanks = 0;
            yield return c;
        }
    }

    internal static IEnumerable<int> ReadQuoted(StructureCharReader reader, long start, long end, bool neon)
    {
        reader.Seek(start);
        int quote = reader.Read();
        if (neon && reader.Peek() == quote && reader.Peek(1) == quote)
        {
            reader.Read(); reader.Read();
            foreach (int value in ReadTriple(reader, end, quote)) yield return value;
            yield break;
        }
        long whitespaceStart = -1, whitespaceEnd = -1;
        while (reader.Position < end)
        {
            long at = reader.Position;
            int c = reader.Read();
            if (c is ' ' or '\t')
            {
                if (whitespaceStart < 0) whitespaceStart = at;
                whitespaceEnd = reader.Position;
                continue;
            }
            if (c is '\r' or '\n') whitespaceStart = -1;
            if (whitespaceStart >= 0)
            {
                long resume = reader.Position;
                reader.Seek(whitespaceStart);
                while (reader.Position < whitespaceEnd) yield return reader.Read();
                reader.Seek(resume);
                whitespaceStart = -1;
            }
            if (c == quote)
            {
                if (quote == '\'' && reader.Peek() == quote) { reader.Read(); yield return quote; continue; }
                yield break;
            }
            if (c is '\r' or '\n')
            {
                if (neon) throw Bad(reader, "NEON multiline strings need triple quotes.");
                foreach (int folded in FoldLine(reader, end, c)) yield return folded;
                continue;
            }
            if (c == '\\' && quote == '"')
            {
                c = reader.Read();
                if (c is '\r' or '\n')
                {
                    foreach (int folded in FoldLine(reader, end, c)) if (folded != ' ') yield return folded;
                    continue;
                }
                c = c switch
                {
                    '0' when !neon => 0, 'a' when !neon => 7, 'b' => '\b', 't' => '\t', 'n' => '\n',
                    'v' when !neon => 11, 'f' => '\f', 'r' => '\r', 'e' when !neon => 27,
                    ' ' when !neon => ' ', '"' => '"', '/' => '/', '\\' => '\\', '_' => 0xA0,
                    'N' when !neon => 0x85, 'L' when !neon => 0x2028, 'P' when !neon => 0x2029,
                    'x' when !neon => Escape(reader, 2), 'u' => Escape(reader, 4), 'U' when !neon => Escape(reader, 8),
                    _ => throw Bad(reader, "Invalid quoted scalar escape."),
                };
            }
            yield return c;
        }
        throw Bad(reader, "The quoted scalar is not closed.");
    }

    private static IEnumerable<int> ReadTriple(StructureCharReader reader, long end, int quote)
    {
        while (reader.Position < end && reader.Peek() is not ('\r' or '\n')) reader.Read();
        Break(reader, end);
        long indent = -1;
        long pendingBreaks = 0;
        while (reader.Position < end)
        {
            long start = reader.Position;
            long spaces = 0;
            while (reader.Peek() is ' ' or '\t') { reader.Read(); spaces++; }
            if (reader.Peek() == quote && reader.Peek(1) == quote && reader.Peek(2) == quote) yield break;
            bool empty = reader.Peek() is '\r' or '\n';
            if (indent < 0 && (spaces != 0 || !empty)) indent = spaces;
            while (pendingBreaks-- > 0) yield return '\n';
            pendingBreaks = 0;
            reader.Seek(start);
            if (spaces >= indent) for (long i = 0; i < indent; i++) reader.Read();
            while (reader.Position < end && reader.Peek() is not ('\r' or '\n'))
            {
                int c = reader.Read();
                if (c == '\\' && quote == '"' && reader.Peek() is not ('\r' or '\n'))
                {
                    int escape = reader.Read();
                    c = escape switch
                    {
                        '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n',
                        'r' => '\r', 't' => '\t', '_' => 0xA0, 'u' => Escape(reader, 4),
                        _ => throw Bad(reader, "Invalid NEON multiline escape."),
                    };
                }
                yield return c;
            }
            if (Break(reader, end)) pendingBreaks++;
        }
        throw Bad(reader, "The NEON multiline string is not closed.");
    }

    private static IEnumerable<int> FoldLine(StructureCharReader reader, long end, int first)
    {
        if (first == '\r' && reader.Peek() == '\n') reader.Read();
        long breaks = 1;
        while (reader.Position < end)
        {
            while (reader.Peek() is ' ' or '\t') reader.Read();
            int c = reader.Peek();
            if (c is not ('\r' or '\n')) break;
            reader.Read();
            if (c == '\r' && reader.Peek() == '\n') reader.Read();
            breaks++;
        }
        if (breaks == 1) yield return ' ';
        else for (long i = 1; i < breaks; i++) yield return '\n';
    }

    private static int Escape(StructureCharReader reader, int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
        {
            int digit = Hex(reader.Read());
            if (digit < 0) throw Bad(reader, "Invalid hexadecimal scalar escape.");
            value = value * 16 + (uint)digit;
        }
        if (value is >= 0xD800 and <= 0xDBFF && count == 4)
        {
            if (reader.Read() != '\\' || reader.Read() != 'u') throw Bad(reader, "A high surrogate needs a low surrogate.");
            int low = EscapeUnit(reader);
            if (low is < 0xDC00 or > 0xDFFF) throw Bad(reader, "Invalid Unicode surrogate pair.");
            return char.ConvertToUtf32((char)value, (char)low);
        }
        if (value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF) throw Bad(reader, "Invalid Unicode scalar escape.");
        return (int)value;
    }

    private static int EscapeUnit(StructureCharReader reader)
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            int digit = Hex(reader.Read());
            if (digit < 0) throw Bad(reader, "Invalid Unicode escape.");
            value = value * 16 + digit;
        }
        return value;
    }

    private static IEnumerable<int> ReadBlock(IStructureIndex index, StructureCharReader reader, long start, long end,
        CancellationToken ct, long nameStart)
    {
        long parentIndent = LineIndent(index, nameStart >= 0 ? nameStart : start, ct, nameStart >= 0);
        reader.Seek(start);
        bool folded = reader.Read() == '>';
        int explicitIndent = 0, chomp = 0;
        while (reader.Position < end && reader.Peek() is not ('\r' or '\n'))
        {
            int c = reader.Read();
            if (c is '+' or '-') chomp = c;
            else if (c is >= '1' and <= '9') explicitIndent = c - '0';
            else if (c == '#') { while (reader.Position < end && reader.Peek() is not ('\r' or '\n')) reader.Read(); }
        }
        Break(reader, end);
        long indent = explicitIndent == 0 ? -1 : parentIndent + explicitIndent;
        long pendingBreaks = 0;
        bool previousMore = false, previousContent = false;
        while (reader.Position < end)
        {
            long line = reader.Position;
            long spaces = 0;
            while (reader.Position < end && reader.Peek() == ' ') { reader.Read(); spaces++; }
            bool empty = reader.Position >= end || reader.Peek() is '\r' or '\n';
            if (!empty && indent < 0) indent = spaces;
            if (empty)
            {
                if (Break(reader, end)) pendingBreaks++;
                continue;
            }
            bool more = spaces > indent;
            if (pendingBreaks > 0)
            {
                if (folded && previousContent && !previousMore && !more)
                {
                    if (pendingBreaks == 1) yield return ' ';
                    else for (long i = 1; i < pendingBreaks; i++) yield return '\n';
                }
                else for (long i = 0; i < pendingBreaks; i++) yield return '\n';
            }
            pendingBreaks = 0;
            reader.Seek(line);
            for (long i = 0; i < indent && reader.Position < end; i++) reader.Read();
            while (reader.Position < end && reader.Peek() is not ('\r' or '\n')) yield return reader.Read();
            if (Break(reader, end)) pendingBreaks++;
            previousContent = true;
            previousMore = more;
        }
        if (chomp == '+') for (long i = 0; i < pendingBreaks; i++) yield return '\n';
        else if (chomp != '-' && pendingBreaks > 0 && previousContent) yield return '\n';
    }

    private static bool Break(StructureCharReader reader, long end)
    {
        if (reader.Position >= end) return false;
        if (reader.Peek() == '\r') reader.Read();
        return reader.Position < end && reader.Consume('\n');
    }

    private static long LineIndent(IStructureIndex index, long start, CancellationToken ct, bool key)
    {
        int unit = index.Codec.Alignment;
        byte[] newline = index.Codec.Encode("\n");
        Span<byte> block = stackalloc byte[4096];
        long at = start;
        long line = index.BomLength;
        while (at > index.BomLength)
        {
            ct.ThrowIfCancellationRequested();
            long from = Math.Max(index.BomLength, at - block.Length);
            from += (unit - (from - index.BomLength) % unit) % unit;
            int count = (int)(at - from);
            int read = index.Source.Read(from, block[..count]);
            if (read != count) throw new EndOfStreamException("The scalar line prefix is unavailable.");
            for (int i = count - newline.Length; i >= 0; i -= unit)
            {
                if (!block.Slice(i, newline.Length).SequenceEqual(newline)) continue;
                line = from + i + newline.Length;
                at = index.BomLength;
                break;
            }
            if (at == index.BomLength) break;
            at = from;
        }
        var prefix = new StructureCharReader(index.Source, index.Codec, index.BomLength, ct);
        prefix.Seek(line);
        long indent = 0;
        while (prefix.Position < start && (key || prefix.Peek() == ' '))
        {
            int c = prefix.Read();
            indent = checked(indent + (c == '\t' ? 8 : 1));
        }
        return indent;
    }

    private static StructureSyntaxException Bad(StructureCharReader reader, string message) =>
        new(StructureIssueKind.Syntax, reader.Position, Math.Min(reader.Length, reader.Position + 1), message);
}

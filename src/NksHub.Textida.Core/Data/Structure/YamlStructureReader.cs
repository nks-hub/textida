namespace NksHub.Textida.Core.Data.Structure;

public static class YamlStructureReader
{
    public static void Parse(StructureParseContext context)
    {
        using var reader = new IndentedStructureReader(context, false);
        reader.Parse();
    }
}

public static class NeonStructureReader
{
    public static void Parse(StructureParseContext context)
    {
        using var reader = new IndentedStructureReader(context, true);
        reader.Parse();
    }
}

internal sealed class IndentedStructureReader : IDisposable
{
    private const int MaxDepth = 256;
    private readonly StructureCharReader reader;
    private readonly IStructureSink sink;
    private readonly bool neon;
    private readonly StructureParseContext context;
    private YamlAnchors? anchors;
    private (long Parent, long Ordinal, long Key)? pendingKey;
    private long column;
    private long lineNumber;
    private bool indentation = true;
    private bool scalarWhitespace;

    private readonly record struct Position(long Offset, long Column, long LineNumber, bool Indentation);
    private Position Mark => new(reader.Position, column, lineNumber, indentation);
    private int Current => reader.Peek();
    private long Offset => reader.Position;

    internal IndentedStructureReader(StructureParseContext context, bool neon)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        reader = context.OpenReader();
        sink = context.Sink;
        this.neon = neon;
        this.context = context;
    }

    private void Restore(Position position)
    {
        reader.Seek(position.Offset);
        column = position.Column;
        lineNumber = position.LineNumber;
        indentation = position.Indentation;
    }

    private int Read()
    {
        int c = reader.Read();
        if (c == '\r')
        {
            if (Current != '\n') throw Error("A lone CR line break is not supported.", StructureIssueKind.UnsupportedSyntax);
        }
        if (c == '\n') { column = 0; lineNumber++; indentation = true; }
        else if (c >= 0)
        {
            if (c == '\t' && indentation && !neon && !scalarWhitespace)
                throw Error("A tab cannot be used as YAML indentation.");
            column = checked(column + (c == '\t' ? 8 : 1));
            if (c is not (' ' or '\t' or '\r')) indentation = false;
            if (c < 32 && c is not ('\r' or '\t')) throw Error("A control character is not allowed in a configuration value.");
            if (!neon && (c is >= 0x7F and <= 0x9F && c != 0x85 || c is 0xFFFE or 0xFFFF))
                throw Error("The source contains a non-printable YAML character.");
        }
        return c;
    }

    private bool Consume(int c)
    {
        if (Current != c) return false;
        Read();
        return true;
    }

    private static bool Blank(int c) => c is -1 or ' ' or '\t' or '\r' or '\n';
    private bool Sequence => Current == '-' && Blank(reader.Peek(1));
    private bool DocumentMarker => !neon && column == 0
        && (reader.StartsWith("---") || reader.StartsWith("...")) && Blank(reader.Peek(3));

    private void Spaces()
    {
        while (Current is ' ' or '\t') Read();
    }

    private void Trivia()
    {
        while (true)
        {
            while (Current is ' ' or '\t' or '\r' or '\n') Read();
            if (Current != '#') return;
            while (Current is not (-1 or '\r' or '\n')) Read();
        }
    }

    private void EndLine()
    {
        Spaces();
        if (Current is not (-1 or '\r' or '\n' or '#')) throw Error("Unexpected text after the value.");
    }

    internal void Parse()
    {
        long document = sink.AddNode(StructureKind.Document, -1, 0, -1, -1, 0, 0, 0);
        Trivia();
        if (DocumentMarker && Current == '-') { Read(); Read(); Read(); EndLine(); Trivia(); }
        if (!neon && Current == '%') throw Error("YAML directives are not supported.", StructureIssueKind.UnsupportedSyntax);
        if (Current < 0 || DocumentMarker) Null(document, -1, -1, 0, 1, Offset);
        else Block(document, -1, -1, 0, 1, -1);
        Trivia();
        if (DocumentMarker && Current == '.') { Read(); Read(); Read(); EndLine(); Trivia(); }
        if (DocumentMarker) throw Error("Multiple YAML documents require a separate document view.", StructureIssueKind.UnsupportedSyntax);
        if (Current >= 0) throw Error("Unexpected text; check collection indentation.");
        sink.CompleteNode(document, 0, reader.Length, reader.Length, 1);
    }

    private long Add(StructureKind kind, long parent, long nameStart, long nameEnd, long ordinal, int depth, long valueStart)
    {
        if (depth > MaxDepth) throw Error("Configuration nesting exceeds 256 levels.", StructureIssueKind.ResourceLimit);
        long id = sink.AddNode(kind, parent, nameStart >= 0 ? nameStart : valueStart, nameStart, nameEnd, valueStart, ordinal, depth);
        if (pendingKey is { } key && key.Parent == parent && key.Ordinal == ordinal)
        {
            sink.SetIntegerKey(id, key.Key);
            pendingKey = null;
        }
        return id;
    }

    private void KeyIndex(long parent, long ordinal, long nameStart, long nameEnd, ref long? maximum)
    {
        if (!neon) return;
        long? key = nameStart < 0 ? maximum is { } previous
                ? previous == long.MaxValue ? throw Error("The implicit NEON integer key overflows signed 64-bit range.") : previous + 1
                : 0
            : NeonStructureKeys.IntegerKey(reader, nameStart, nameEnd);
        if (key is not { } integer) return;
        if (maximum is null || integer > maximum) maximum = integer;
        pendingKey = (parent, ordinal, integer);
    }

    private long Null(long parent, long nameStart, long nameEnd, long ordinal, int depth, long start)
    {
        long id = Add(StructureKind.JsonNull, parent, nameStart, nameEnd, ordinal, depth, start);
        sink.CompleteNode(id, start, start, start, 0);
        return start;
    }

    private long Block(long parent, long nameStart, long nameEnd, long ordinal, int depth, long parentIndent)
    {
        if (!neon && Current is '&' or '!') return Tagged(parent, nameStart, nameEnd, ordinal, depth, parentIndent);
        if (Sequence || LooksLikeKey(false)) return Collection(parent, nameStart, nameEnd, ordinal, depth, column);
        return Value(parent, nameStart, nameEnd, ordinal, depth, parentIndent, false);
    }

    private bool Separator(bool flow, bool quoted = false) => Current == '=' && neon
        || Current == ':' && (quoted || Blank(reader.Peek(1)) || flow && reader.Peek(1) is ',' or ']' or '}');

    private bool LooksLikeKey(bool flow)
    {
        var saved = Mark;
        try
        {
            if (Current is '[' or '{' or '|' or '>' or '&' or '!' or '*') return false;
            if (Current is '\'' or '"')
            {
                Quoted(); Spaces();
                return Separator(flow, true);
            }
            bool previousBlank = false;
            while (Current is not (-1 or '\r' or '\n'))
            {
                if (Separator(flow)) return true;
                if (Current == '#' && previousBlank || flow && Current is ',' or ']' or '}' or ')') return false;
                previousBlank = Current is ' ' or '\t';
                Read();
            }
            return false;
        }
        finally { Restore(saved); }
    }

    private (long Start, long End) Key(bool flow)
    {
        if (!neon && Current is '@' or '`' or '%') throw Error("A reserved YAML indicator cannot start a plain key.");
        long start = Offset, end;
        if (Current is '\'' or '"') { Quoted(); end = Offset; Spaces(); }
        else
        {
            if (Current is '?' or '[' or '{' or '&' or '!' or '*')
                throw Error("Complex mapping keys are not supported.", StructureIssueKind.UnsupportedSyntax);
            end = start;
            while (Current is not (-1 or '\r' or '\n') && !Separator(flow))
            {
                if (flow && Current is ',' or ']' or '}' or ')') break;
                int c = Read();
                if (c is not (' ' or '\t')) end = Offset;
            }
        }
        if (end == start || !Separator(flow, true)) throw Error("A nonempty scalar key followed by ':' or '=' is expected.");
        Read();
        return (start, end);
    }

    private long Collection(long parent, long nameStart, long nameEnd, long ordinal, int depth, long indent)
    {
        long start = Offset, count = 0, end = start;
        long? maximum = null;
        bool sequence = Sequence;
        bool mixed = false;
        long id = Add(sequence ? StructureKind.JsonArray : StructureKind.JsonObject,
            parent, nameStart, nameEnd, ordinal, depth, start);
        while (Current >= 0 && !DocumentMarker && column == indent)
        {
            if (Sequence)
            {
                if (!sequence && !neon) break;
                Read();
                long empty = Offset;
                Spaces();
                KeyIndex(id, count, -1, -1, ref maximum);
                end = NestedValue(id, -1, -1, count++, depth + 1, indent, empty, true);
            }
            else
            {
                if (sequence && !neon || !LooksLikeKey(false)) break;
                if (sequence && !mixed) { sink.SetKind(id, StructureKind.JsonObject); mixed = true; }
                var name = Key(false);
                long empty = Offset;
                Spaces();
                KeyIndex(id, count, name.Start, name.End, ref maximum);
                end = NestedValue(id, name.Start, name.End, count++, depth + 1, indent, empty, false);
            }
            Trivia();
            if (Current >= 0 && !DocumentMarker && column > indent) throw Error("The indentation does not match the collection.");
        }
        sink.CompleteNode(id, start, end, end, count);
        return end;
    }

    private long NestedValue(long parent, long nameStart, long nameEnd, long ordinal, int depth, long indent, long empty, bool sequenceItem)
    {
        if (Current is -1 or '\r' or '\n' or '#')
        {
            Trivia();
            if (Current >= 0 && !DocumentMarker && (column > indent || !neon && !sequenceItem && column == indent && Sequence))
                return Block(parent, nameStart, nameEnd, ordinal, depth, indent);
            return Null(parent, nameStart, nameEnd, ordinal, depth, empty);
        }
        if (sequenceItem) return Block(parent, nameStart, nameEnd, ordinal, depth, indent);
        if (Sequence) throw Error("A block sequence must start on a new line after a mapping key.");
        long end = Value(parent, nameStart, nameEnd, ordinal, depth, indent, false);
        if (!indentation) EndLine();
        return end;
    }

    private long Value(long parent, long nameStart, long nameEnd, long ordinal, int depth, long indent, bool flow)
    {
        if (depth > MaxDepth) throw Error("Configuration nesting exceeds 256 levels.", StructureIssueKind.ResourceLimit);
        if (!neon && Current is '&' or '!') return Tagged(parent, nameStart, nameEnd, ordinal, depth, indent);
        if (Current is '[' or '{') return Flow(parent, nameStart, nameEnd, ordinal, depth);
        long start = Offset;
        if (!neon && Current is '|' or '>')
        {
            if (flow) throw Error("YAML block scalars are only allowed in block values.");
            long end = BlockScalar(indent);
            long id = Add(StructureKind.JsonString, parent, nameStart, nameEnd, ordinal, depth, start);
            sink.CompleteNode(id, start, end, end, 0);
            return end;
        }
        if (Current is '\'' or '"')
        {
            Quoted();
            return Scalar(StructureKind.JsonString, parent, nameStart, nameEnd, ordinal, depth, start, Offset);
        }
        if (Current == '*' && !neon)
        {
            Read();
            long symbol = Offset;
            while (!Blank(Current) && Current is not (',' or '[' or ']' or '{' or '}')) Read();
            if (Offset == symbol) throw Error("An alias needs an anchor name.");
            if (anchors is null || !anchors.Contains(symbol, Offset))
                throw new StructureSyntaxException(StructureIssueKind.Syntax, start, Offset, "The YAML alias has no preceding anchor definition.");
            return Scalar(StructureKind.ConfigReference, parent, nameStart, nameEnd, ordinal, depth, start, Offset);
        }
        if (!neon && Current is '@' or '`' or '%') throw Error("A reserved YAML indicator cannot start a plain value.");
        if (Current is -1 or ',' or ']' or '}' or ')' || Current == '?' && Blank(reader.Peek(1)))
            throw Error("A scalar value is expected.");
        long endPlain = Plain(indent, flow);
        if (endPlain == start) throw Error("A scalar value is expected.");
        if (neon && Current == '(') return Entity(parent, nameStart, nameEnd, ordinal, depth, start);
        var kind = ConfigScalarText.Classify(reader, start, endPlain, neon);
        if (neon && (ReaderAt(start) == '@' || ReaderAt(start) == '%')) kind = StructureKind.ConfigReference;
        return Scalar(kind, parent, nameStart, nameEnd, ordinal, depth, start, endPlain);
    }

    private int ReaderAt(long start)
    {
        long at = Offset;
        reader.Seek(start); int c = reader.Peek(); reader.Seek(at);
        return c;
    }

    private long Scalar(StructureKind kind, long parent, long nameStart, long nameEnd, long ordinal, int depth, long start, long end)
    {
        long id = Add(kind, parent, nameStart, nameEnd, ordinal, depth, start);
        sink.CompleteNode(id, start, end, end, 0);
        return end;
    }

    private long Plain(long indent, bool flow)
    {
        long end = Offset;
        bool previousBlank = false;
        while (Current >= 0)
        {
            int c = Current;
            if (flow && c is ',' or '[' or ']' or '{' or '}' or ')' || neon && c == '(') break;
            if (c == '#' && (neon || previousBlank) || Separator(flow)) break;
            if (c is '\r' or '\n')
            {
                var saved = Mark;
                while (Current is ' ' or '\t' or '\r' or '\n') Read();
                if (flow && !neon && Current is not (',' or ']' or '}' or ')')) { previousBlank = true; continue; }
                if (!flow && !neon && Current >= 0 && !DocumentMarker && column > indent && !Sequence && !LooksLikeKey(false))
                { previousBlank = true; continue; }
                Restore(saved);
                break;
            }
            Read();
            previousBlank = c is ' ' or '\t';
            if (!previousBlank) end = Offset;
        }
        return end;
    }

    private void Quoted()
    {
        bool previous = scalarWhitespace;
        scalarWhitespace = true;
        try { ReadQuotedCore(); }
        finally { scalarWhitespace = previous; }
    }

    private void ReadQuotedCore()
    {
        long start = Offset;
        int quote = Read();
        if (neon && Current == quote && reader.Peek(1) == quote)
        {
            Read(); Read(); Spaces();
            if (Current is not ('\r' or '\n')) throw Error("A NEON multiline string must start on the next line.");
            while (true)
            {
                while (Current is not (-1 or '\n')) Read();
                if (!Consume('\n')) throw Error("The NEON multiline string is not closed.");
                var line = Mark;
                Spaces();
                if (Current == quote && reader.Peek(1) == quote && reader.Peek(2) == quote)
                {
                    Read(); Read(); Read();
                    long endTriple = Offset;
                    EndLine();
                    foreach (int _ in ConfigScalarText.ReadQuoted(reader, start, endTriple, true)) { }
                    reader.Seek(endTriple);
                    return;
                }
                Restore(line);
            }
        }
        while (true)
        {
            int c = Read();
            if (c < 0) throw Error("The quoted scalar is not closed.");
            if (c == quote)
            {
                if (quote == '\'' && Current == quote) { Read(); continue; }
                break;
            }
            if (c == '\\' && quote == '"')
            {
                int escape = Read();
                if (escape < 0) throw Error("The quoted escape is incomplete.");
                if (escape is 'x' or 'u' or 'U')
                {
                    int digits = escape == 'x' ? 2 : escape == 'u' ? 4 : 8;
                    for (int i = 0; i < digits; i++) if (ConfigScalarText.Hex(Read()) < 0) throw Error("Invalid hexadecimal scalar escape.");
                }
                else if (escape is not ('0' or 'a' or 'b' or 't' or 'n' or 'v' or 'f' or 'r' or 'e' or ' ' or '"' or '/' or '\\' or 'N' or '_' or 'L' or 'P' or '\r' or '\n'))
                    throw Error("Unknown quoted scalar escape.");
            }
        }
        long end = Offset;
        foreach (int _ in ConfigScalarText.ReadQuoted(reader, start, end, neon)) { }
        reader.Seek(end);
    }

    private long BlockScalar(long parentIndent)
    {
        Read();
        int indicator = 0;
        bool chomp = false;
        for (int i = 0; i < 2; i++)
        {
            if (Current is '+' or '-' && !chomp) { chomp = true; Read(); }
            else if (Current is >= '1' and <= '9' && indicator == 0) indicator = Read() - '0';
            else break;
        }
        EndLine();
        while (Current is not (-1 or '\n')) Read();
        if (Current == '\n') Read();
        long contentIndent = indicator == 0 ? -1 : Math.Max(0, parentIndent) + indicator;
        long leadingBlankIndent = 0;
        long end = Offset;
        while (Current >= 0)
        {
            var line = Mark;
            while (Current == ' ') Read();
            bool empty = Current is '\r' or '\n' or -1;
            if (!empty)
            {
                if (contentIndent < 0)
                {
                    contentIndent = column;
                    if (leadingBlankIndent > contentIndent) throw Error("A leading empty scalar line is over-indented.");
                }
                if (column <= parentIndent || column < contentIndent) { Restore(line); break; }
                indentation = false;
            }
            else if (contentIndent < 0) leadingBlankIndent = Math.Max(leadingBlankIndent, column);
            while (Current is not (-1 or '\n')) Read();
            if (Current == '\n') Read();
            end = Offset;
        }
        return end;
    }

    private long Tagged(long parent, long nameStart, long nameEnd, long ordinal, int depth, long indent)
    {
        long start = Offset;
        while (Current is '&' or '!')
        {
            int symbol = Read();
            if (symbol == '!' && Consume('<'))
            {
                while (Current is not (-1 or '\r' or '\n' or '>')) Read();
                if (!Consume('>')) throw Error("The verbatim YAML tag is not closed.");
            }
            else
            {
                long value = Offset;
                while (!Blank(Current) && Current is not (',' or '[' or ']' or '{' or '}')) Read();
                if (symbol == '&' && value == Offset) throw Error("An anchor needs a name.");
                if (symbol == '&')
                {
                    anchors ??= new YamlAnchors(context.Source, context.CancellationToken);
                    anchors.Define(value, Offset);
                }
            }
            Spaces();
        }
        long id = Add(StructureKind.ConfigTaggedValue, parent, nameStart, nameEnd, ordinal, depth, start);
        long end = NestedValue(id, -1, -1, 0, depth + 1, indent, Offset, true);
        sink.CompleteNode(id, start, end, end, 1);
        return end;
    }

    private long Entity(long parent, long nameStart, long nameEnd, long ordinal, int depth, long start)
    {
        long id = Add(StructureKind.ConfigEntity, parent, nameStart, nameEnd, ordinal, depth, start);
        long count = Arguments(id, depth, ')', false);
        long end = Offset;
        while (true)
        {
            var saved = Mark;
            Spaces();
            long childStart = Offset;
            while (Current >= 0 && !Blank(Current) && Current is not ('(' or ',' or ']' or '}' or ')' or '#')) Read();
            if (Offset == childStart || Current != '(') { Restore(saved); break; }
            Entity(id, -1, -1, count++, depth + 1, childStart);
            end = Offset;
        }
        sink.CompleteNode(id, start, end, end, count);
        return end;
    }

    private long Flow(long parent, long nameStart, long nameEnd, long ordinal, int depth)
    {
        long start = Offset;
        bool map = Current == '{';
        long id = Add(map ? StructureKind.JsonObject : StructureKind.JsonArray, parent, nameStart, nameEnd, ordinal, depth, start);
        long count = Arguments(id, depth, map ? '}' : ']', map);
        sink.CompleteNode(id, start, Offset, Offset, count);
        return Offset;
    }

    private long Arguments(long id, int depth, int close, bool map)
    {
        Read(); Trivia();
        long count = 0;
        long? maximum = null;
        while (!Consume(close))
        {
            if (Current < 0) throw Error("The flow collection or entity is not closed.");
            long ns = -1, ne = -1;
            if (!neon && !map && LooksLikeKey(true))
                throw Error("Compact mappings in YAML flow sequences are not supported.", StructureIssueKind.UnsupportedSyntax);
            if (map && !neon || neon && LooksLikeKey(true)) { (ns, ne) = Key(true); Trivia(); }
            if (neon && close == ']' && ns >= 0 && !map) { sink.SetKind(id, StructureKind.JsonObject); map = true; }
            KeyIndex(id, count, ns, ne, ref maximum);
            if ((Current == close || Current == ',') && ns >= 0) Null(id, ns, ne, count++, depth + 1, Offset);
            else { Value(id, ns, ne, count++, depth + 1, -1, true); }
            long lastLine = lineNumber;
            Trivia();
            if (Current == close) continue;
            if (Consume(',')) { Trivia(); continue; }
            if (neon && lineNumber > lastLine) continue;
            throw Error("A comma or the closing collection delimiter is expected.");
        }
        return count;
    }

    private StructureSyntaxException Error(string message, StructureIssueKind kind = StructureIssueKind.Syntax) =>
        new(kind, Offset, Math.Min(reader.Length, Offset + 1), message);

    public void Dispose() => anchors?.Dispose();
}

namespace NksHub.Textida.Core.Data.Structure;

public static class JsonStructureReader
{
    public const int MaxDepth = 65_536;

    public static void Parse(StructureParseContext context, bool jsonc = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        new Parser(context, jsonc).Parse();
    }

    private enum State : byte { ObjectFirst, ObjectKey, ObjectSeparator, ArrayFirst, ArrayValue, ArraySeparator }

    private struct Frame(long id, long valueStart, bool isObject)
    {
        public readonly long Id = id;
        public readonly long ValueStart = valueStart;
        public readonly bool IsObject = isObject;
        public long Children;
        public State State = isObject ? State.ObjectFirst : State.ArrayFirst;
    }

    private sealed class Parser(StructureParseContext context, bool jsonc)
    {
        private readonly StructureCharReader reader = context.OpenReader();
        private readonly IStructureSink sink = context.Sink;
        private readonly List<Frame> frames = [];

        public void Parse()
        {
            long document = sink.AddNode(StructureKind.Document, -1, 0, -1, -1, 0, 0, 0);
            SkipTrivia();
            ReadValue(document, -1, -1, 0, 1);
            while (frames.Count > 0)
            {
                SkipTrivia();
                var frame = frames[^1];
                switch (frame.State)
                {
                    case State.ObjectFirst:
                    case State.ObjectKey:
                        if (reader.Peek() == '}')
                        {
                            if (frame.State == State.ObjectKey && !jsonc) throw Here("A trailing comma is not allowed in JSON.");
                            Close(frame);
                        }
                        else ReadMember(frame);
                        break;
                    case State.ArrayFirst:
                    case State.ArrayValue:
                        if (reader.Peek() == ']')
                        {
                            if (frame.State == State.ArrayValue && !jsonc) throw Here("A trailing comma is not allowed in JSON.");
                            Close(frame);
                        }
                        else ReadChild(frame, -1, -1);
                        break;
                    case State.ObjectSeparator:
                    case State.ArraySeparator:
                        if (reader.Peek() == (frame.IsObject ? '}' : ']')) Close(frame);
                        else if (reader.Consume(','))
                        {
                            frame.State = frame.IsObject ? State.ObjectKey : State.ArrayValue;
                            frames[^1] = frame;
                        }
                        else throw Here(frame.IsObject ? "A comma or '}' is expected." : "A comma or ']' is expected.");
                        break;
                }
            }
            SkipTrivia();
            if (reader.Peek() >= 0) throw Here("Unexpected text after the JSON value.");
            sink.CompleteNode(document, 0, reader.Length, reader.Length, 1);
        }

        private void ReadMember(Frame frame)
        {
            if (reader.Peek() != '"') throw Here("An object property name in double quotes is expected.");
            long nameStart = reader.Position;
            ReadString();
            long nameEnd = reader.Position;
            SkipTrivia();
            if (!reader.Consume(':')) throw Here("A colon is expected after the property name.");
            SkipTrivia();
            ReadChild(frame, nameStart, nameEnd);
        }

        private void ReadChild(Frame frame, long nameStart, long nameEnd)
        {
            long ordinal = frame.Children++;
            frame.State = frame.IsObject ? State.ObjectSeparator : State.ArraySeparator;
            frames[^1] = frame;
            ReadValue(frame.Id, nameStart, nameEnd, ordinal, frames.Count + 1);
        }

        private void ReadValue(long parent, long nameStart, long nameEnd, long ordinal, int depth)
        {
            long start = reader.Position;
            int first = reader.Peek();
            var kind = first switch
            {
                '{' => StructureKind.JsonObject,
                '[' => StructureKind.JsonArray,
                '"' => StructureKind.JsonString,
                't' or 'f' => StructureKind.JsonBoolean,
                'n' => StructureKind.JsonNull,
                '-' or >= '0' and <= '9' => StructureKind.JsonNumber,
                _ => throw Here("A JSON value is expected."),
            };
            if (depth > MaxDepth) throw Here("JSON nesting exceeds the parser depth limit.", StructureIssueKind.ResourceLimit);
            long id = sink.AddNode(kind, parent, nameStart >= 0 ? nameStart : start, nameStart, nameEnd, start, ordinal, depth);
            if (kind is StructureKind.JsonObject or StructureKind.JsonArray)
            {
                reader.Read();
                frames.Add(new Frame(id, start, kind == StructureKind.JsonObject));
                return;
            }
            switch (kind)
            {
                case StructureKind.JsonString: ReadString(); break;
                case StructureKind.JsonNumber: ReadNumber(); break;
                case StructureKind.JsonBoolean: ReadLiteral(first == 't' ? "true" : "false"); break;
                case StructureKind.JsonNull: ReadLiteral("null"); break;
            }
            sink.CompleteNode(id, start, reader.Position, reader.Position, 0);
        }

        private void Close(Frame frame)
        {
            reader.Read();
            sink.CompleteNode(frame.Id, frame.ValueStart, reader.Position, reader.Position, frame.Children);
            frames.RemoveAt(frames.Count - 1);
        }

        private void ReadLiteral(string literal)
        {
            foreach (char expected in literal)
            {
                if (!reader.Consume(expected)) throw Here("Invalid JSON literal.");
            }
        }

        private static bool Digit(int value) => value is >= '0' and <= '9';

        private void ReadNumber()
        {
            reader.Consume('-');
            if (!Digit(reader.Peek())) throw Here("A digit is expected in the JSON number.");
            if (reader.Consume('0'))
            {
                if (Digit(reader.Peek())) throw Here("Leading zeroes are not allowed in JSON numbers.");
            }
            else while (Digit(reader.Peek())) reader.Read();
            if (reader.Consume('.'))
            {
                if (!Digit(reader.Peek())) throw Here("A digit is expected after the decimal point.");
                while (Digit(reader.Peek())) reader.Read();
            }
            if (reader.Peek() is 'e' or 'E')
            {
                reader.Read();
                if (reader.Peek() is '+' or '-') reader.Read();
                if (!Digit(reader.Peek())) throw Here("A digit is expected in the exponent.");
                while (Digit(reader.Peek())) reader.Read();
            }
        }

        private void ReadString()
        {
            long open = reader.Position;
            reader.Read();
            while (true)
            {
                long at = reader.Position;
                int value = reader.Read();
                if (value < 0) throw Syntax(open, reader.Position, "The JSON string is not closed.");
                if (value == '"') return;
                if (value < 0x20) throw Syntax(at, reader.Position, "A control character in a JSON string must be escaped.");
                if (value != '\\') continue;
                int escape = reader.Read();
                if (escape is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't') continue;
                if (escape != 'u') throw Syntax(at, reader.Position, "Invalid JSON string escape.");
                int unit = ReadHexUnit();
                if (unit is >= 0xD800 and <= 0xDBFF)
                {
                    if (!reader.Consume('\\') || !reader.Consume('u')) throw Syntax(at, reader.Position, "An escaped high surrogate needs an escaped low surrogate.");
                    int low = ReadHexUnit();
                    if (low is < 0xDC00 or > 0xDFFF) throw Syntax(at, reader.Position, "Invalid escaped surrogate pair.");
                }
                else if (unit is >= 0xDC00 and <= 0xDFFF) throw Syntax(at, reader.Position, "An escaped low surrogate has no high surrogate.");
            }
        }

        private int ReadHexUnit()
        {
            int unit = 0;
            for (int i = 0; i < 4; i++)
            {
                int value = reader.Peek();
                int digit = value switch
                {
                    >= '0' and <= '9' => value - '0',
                    >= 'a' and <= 'f' => value - 'a' + 10,
                    >= 'A' and <= 'F' => value - 'A' + 10,
                    _ => throw Here("A JSON Unicode escape needs four hexadecimal digits."),
                };
                reader.Read();
                unit = unit * 16 + digit;
            }
            return unit;
        }

        private void SkipTrivia()
        {
            while (true)
            {
                reader.SkipWhitespace();
                if (!jsonc || reader.Peek() != '/') return;
                if (reader.Peek(1) == '/')
                {
                    reader.Read();
                    reader.Read();
                    while (reader.Peek() is not (-1 or '\r' or '\n')) reader.Read();
                }
                else if (reader.Peek(1) == '*')
                {
                    long open = reader.Position;
                    reader.Read();
                    reader.Read();
                    while (!reader.StartsWith("*/"))
                    {
                        if (reader.Read() < 0) throw Syntax(open, reader.Position, "The JSONC comment is not closed.");
                    }
                    reader.Read();
                    reader.Read();
                }
                else return;
            }
        }

        private StructureSyntaxException Here(string message, StructureIssueKind kind = StructureIssueKind.Syntax)
        {
            long start = reader.Position;
            reader.Read();
            return new(kind, start, reader.Position, message);
        }

        private static StructureSyntaxException Syntax(long start, long end, string message) => new(StructureIssueKind.Syntax, start, end, message);
    }
}

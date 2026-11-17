using System.Globalization;
using System.Numerics;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Data.Structure;

public sealed record StructurePreview(string Text, bool Truncated);

public sealed partial class StructureQuery
{
    /// <summary>Returns the full source range of one query result, including all adjacent XML text and CDATA parts.</summary>
    public static ByteRange ResultRange(IStructureIndex index, long id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ct.ThrowIfCancellationRequested();
        var node = index.ReadNode(id);
        if (index.Format != StructureFormat.Xml || !StructureXmlNodes.IsText(node)) return node.Range;
        var first = StructureXmlNodes.LogicalTextStart(index, node, ct);
        return new(first.Start, StructureXmlNodes.LogicalTextEnd(index, node, ct).End);
    }

    /// <summary>Reads a bounded query result value. XML text groups and element string values follow the XPath data model.</summary>
    public static StructurePreview ReadResultValue(IStructureIndex index, long id, int maxChars = 256, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (maxChars is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxChars));
        ct.ThrowIfCancellationRequested();
        var node = index.ReadNode(id);
        if (index.Format == StructureFormat.Xml) node = StructureXmlNodes.LogicalTextStart(index, node, ct);
        return new ValueReader(index, ct).ResultPreview(node, maxChars);
    }

    public static StructurePreview ReadPreview(IStructureIndex index, StructureNode node, bool name = false,
        int maxChars = 256, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (maxChars is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxChars));
        ct.ThrowIfCancellationRequested();
        return new ValueReader(index, ct).Preview(node, name, maxChars);
    }

    internal sealed record NumberValue(bool Negative, string Digits, BigInteger Exponent)
    {
        public static NumberValue Parse(string text)
        {
            bool negative = text[0] == '-';
            int start = negative ? 1 : 0;
            int e = text.IndexOfAny(['e', 'E']);
            int end = e < 0 ? text.Length : e;
            int dot = text.IndexOf('.', start, end - start);
            int fraction = dot < 0 ? 0 : end - dot - 1;
            var digits = new StringBuilder(end - start);
            for (int i = start; i < end; i++) if (text[i] != '.') digits.Append(text[i]);
            int first = 0, last = digits.Length;
            while (first < last && digits[first] == '0') first++;
            while (last > first && digits[last - 1] == '0') last--;
            if (first == last) return new(false, "", BigInteger.Zero);
            BigInteger exponent = e < 0 ? BigInteger.Zero
                : BigInteger.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return new(negative, digits.ToString(first, last - first), exponent - fraction + digits.Length - last);
        }
    }

    internal sealed class ValueReader
    {
        private readonly IStructureIndex index;
        private readonly CancellationToken ct;
        private readonly StructureCharReader reader;

        public ValueReader(IStructureIndex index, CancellationToken ct)
        {
            this.index = index;
            this.ct = ct;
            reader = new(index.Source, index.Codec, index.BomLength, ct);
        }

        public bool JsonNameEquals(StructureNode node, string name) =>
            Equal(ReadName(node), name);

        public bool XmlLocalNameEquals(StructureNode node, string name)
        {
            reader.Seek(node.NameStart);
            long localStart = node.NameStart;
            while (reader.Position < node.NameEnd)
                if (reader.Read() == ':') localStart = reader.Position;
            return Equal(Raw(localStart, node.NameEnd), name);
        }

        public bool XmlValueEquals(StructureNode node, string value) =>
            Equal(XmlText(node.ValueStart, node.ValueEnd, attribute: node.Kind is StructureKind.XmlAttribute or StructureKind.XmlNamespace), value);

        public bool XmlStringValueEquals(StructureNode node, string value) => Equal(XmlStringValue(node), value);

        public IEnumerable<int> ReadName(StructureNode node) => !node.HasName ? []
            : index.Format is StructureFormat.Yaml or StructureFormat.Neon
                ? ConfigScalarText.Read(index, reader, node.NameStart, node.NameEnd, name: true, ct)
            : IsJson(node.Kind) ? JsonString(node.NameStart, node.NameEnd) : Raw(node.NameStart, node.NameEnd);

        public IEnumerable<int> ReadValue(StructureNode node) => node.Kind == StructureKind.JsonString
            ? index.Format is StructureFormat.Yaml or StructureFormat.Neon
                ? ConfigScalarText.Read(index, reader, node.ValueStart, node.ValueEnd, ct: ct, nameStart: node.NameStart)
                : JsonString(node.ValueStart, node.ValueEnd)
            : node.Kind == StructureKind.JsonBoolean && index.Format is StructureFormat.Yaml or StructureFormat.Neon
                ? ConfigBoolean(node)
            : node.Kind is StructureKind.XmlAttribute or StructureKind.XmlNamespace or StructureKind.XmlText
                ? XmlText(node.ValueStart, node.ValueEnd, node.Kind != StructureKind.XmlText)
                : Raw(node.ValueStart, node.ValueEnd, node.Kind == StructureKind.XmlCData);

        public StructurePreview Preview(StructureNode node, bool name, int maxChars)
        {
            if (name && !node.HasName) return new("", false);
            return PreviewContent(name ? ReadName(node) : ReadValue(node), maxChars);
        }

        public StructurePreview ResultPreview(StructureNode node, int maxChars) =>
            PreviewContent(index.Format == StructureFormat.Xml ? XmlStringValue(node) : ReadValue(node), maxChars);

        private StructurePreview PreviewContent(IEnumerable<int> content, int maxChars)
        {
            var text = new StringBuilder(Math.Min(maxChars, 256));
            foreach (int value in content)
            {
                ct.ThrowIfCancellationRequested();
                int width = value > char.MaxValue ? 2 : 1;
                if (text.Length + width > maxChars) return new(text.ToString(), true);
                if (width == 1) text.Append((char)value);
                else text.Append(char.ConvertFromUtf32(value));
            }
            return new(text.ToString(), false);
        }

        public bool JsonValueEquals(StructureNode node, Literal literal) => literal.Kind switch
        {
            LiteralKind.String => node.Kind == StructureKind.JsonString && Equal(ReadValue(node), literal.Text),
            LiteralKind.Number => node.Kind == StructureKind.JsonNumber && NumberEquals(node, literal.Number!),
            LiteralKind.Boolean => node.Kind == StructureKind.JsonBoolean && Equal(ReadValue(node), literal.Text),
            LiteralKind.Null => node.Kind == StructureKind.JsonNull,
            _ => false,
        };

        private IEnumerable<int> ConfigBoolean(StructureNode node)
        {
            reader.Seek(node.ValueStart);
            bool value = reader.Peek() is 't' or 'T' or 'y' or 'Y';
            foreach (char c in value ? "true" : "false") yield return c;
        }

        private bool Equal(IEnumerable<int> actual, string expected)
        {
            var runes = expected.EnumerateRunes().GetEnumerator();
            foreach (int rune in actual)
            {
                ct.ThrowIfCancellationRequested();
                if (!runes.MoveNext() || runes.Current.Value != rune) return false;
            }
            return !runes.MoveNext();
        }

        private IEnumerable<int> Raw(long start, long end, bool normalizeXmlLines = false)
        {
            reader.Seek(start);
            while (reader.Position < end)
            {
                int value = reader.Read();
                if (normalizeXmlLines && value == '\r')
                {
                    if (reader.Position < end && reader.Peek() == '\n') reader.Read();
                    value = '\n';
                }
                yield return value;
            }
        }

        private IEnumerable<int> JsonString(long start, long end)
        {
            reader.Seek(start);
            if (reader.Read() != '"') throw new InvalidDataException("A JSON string span does not begin with a quote.");
            while (reader.Position < end)
            {
                int value = reader.Read();
                if (value == '"') yield break;
                if (value == '\\')
                {
                    value = reader.Read();
                    value = value switch
                    {
                        '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                        'u' => ReadJsonEscape(),
                        _ => throw new InvalidDataException("Invalid escape in a parsed JSON string span."),
                    };
                }
                yield return value;
            }
            throw new InvalidDataException("A JSON string span has no closing quote.");
        }

        private int ReadJsonEscape()
        {
            int high = ReadHex(4);
            if (high is < 0xD800 or > 0xDFFF) return high;
            if (high > 0xDBFF || reader.Read() != '\\' || reader.Read() != 'u')
                throw new InvalidDataException("Invalid surrogate in a parsed JSON string span.");
            int low = ReadHex(4);
            if (low is < 0xDC00 or > 0xDFFF) throw new InvalidDataException("Invalid JSON surrogate pair.");
            return char.ConvertToUtf32((char)high, (char)low);
        }

        private int ReadHex(int digits)
        {
            int result = 0;
            for (int i = 0; i < digits; i++)
            {
                int c = reader.Read();
                int digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10
                    : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0) throw new InvalidDataException("Invalid hexadecimal escape in a parsed source span.");
                result = (result << 4) | digit;
            }
            return result;
        }

        private IEnumerable<int> XmlText(long start, long end, bool attribute)
        {
            reader.Seek(start);
            while (reader.Position < end)
            {
                int value = reader.Read();
                if (value == '&') value = ReadXmlEntity(end);
                else
                {
                    if (value == '\r')
                    {
                        if (reader.Position < end && reader.Peek() == '\n') reader.Read();
                        value = '\n';
                    }
                    if (attribute && value is '\t' or '\n') value = ' ';
                }
                yield return value;
            }
        }

        private int ReadXmlEntity(long end)
        {
            if (reader.Consume('#'))
            {
                bool hex = reader.Consume('x');
                int value = 0;
                while (reader.Position < end && reader.Peek() != ';')
                {
                    int c = reader.Read();
                    int digit = c is >= '0' and <= '9' ? c - '0' : hex && c is >= 'a' and <= 'f' ? c - 'a' + 10
                        : hex && c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0 || value > 0x10FFFF / (hex ? 16 : 10))
                        throw new InvalidDataException("Invalid numeric XML entity in a parsed span.");
                    value = value * (hex ? 16 : 10) + digit;
                }
                if (!reader.Consume(';') || !Rune.IsValid(value)) throw new InvalidDataException("Invalid XML entity.");
                return value;
            }
            var name = new StringBuilder(5);
            while (reader.Position < end && reader.Peek() != ';' && name.Length <= 4) name.Append((char)reader.Read());
            if (!reader.Consume(';')) throw new InvalidDataException("Invalid named XML entity in a parsed span.");
            return name.ToString() switch
            {
                "amp" => '&', "lt" => '<', "gt" => '>', "quot" => '"', "apos" => '\'',
                _ => throw new InvalidDataException("Unknown XML entity in a parsed span."),
            };
        }

        private IEnumerable<int> XmlStringValue(StructureNode node)
        {
            if (node.Kind is StructureKind.XmlElement or StructureKind.Document)
            {
                for (long id = node.Id + 1; id <= node.LastDescendantId && id < index.Count; id++)
                {
                    ct.ThrowIfCancellationRequested();
                    var child = index.ReadNode(id);
                    if (child.Kind == StructureKind.XmlText && index.ReadNode(child.ParentId).Kind == StructureKind.XmlElement)
                        foreach (int value in XmlText(child.ValueStart, child.ValueEnd, false)) yield return value;
                    else if (child.Kind == StructureKind.XmlCData)
                        foreach (int value in Raw(child.ValueStart, child.ValueEnd, true)) yield return value;
                }
            }
            else if (StructureXmlNodes.IsText(node))
            {
                for (var part = node; ;)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = part.Kind == StructureKind.XmlText ? XmlText(part.ValueStart, part.ValueEnd, false)
                        : Raw(part.ValueStart, part.ValueEnd, true);
                    foreach (int value in content) yield return value;
                    if (part.Id + 1 >= index.Count) break;
                    var next = index.ReadNode(part.Id + 1);
                    if (next.ParentId != node.ParentId || !StructureXmlNodes.IsText(next)) break;
                    part = next;
                }
            }
            else if (node.Kind is StructureKind.XmlAttribute or StructureKind.XmlNamespace)
            {
                foreach (int value in XmlText(node.ValueStart, node.ValueEnd, node.Kind != StructureKind.XmlText)) yield return value;
            }
            else
            {
                foreach (int value in Raw(node.ValueStart, node.ValueEnd, true)) yield return value;
            }
        }

        private bool NumberEquals(StructureNode node, NumberValue expected)
        {
            reader.Seek(node.ValueStart);
            bool negative = reader.Consume('-');
            if (index.Format is StructureFormat.Yaml or StructureFormat.Neon)
            {
                if (!negative) reader.Consume('+');
                if (reader.Peek() == '.' && reader.Peek(1) is not (>= '0' and <= '9')) return false;
                if (reader.Peek() == '0' && reader.Peek(1) is 'x' or 'o' or 'b') return RadixEquals(node, expected);
            }
            bool fraction = false, nonzero = false;
            long fractionDigits = 0, trailingZeros = 0;
            var digits = new StringBuilder(Math.Min(expected.Digits.Length, 256));
            while (reader.Position < node.ValueEnd && reader.Peek() is not ('e' or 'E'))
            {
                int c = reader.Read();
                if (c == '.') { fraction = true; continue; }
                if (fraction) fractionDigits++;
                if (c == '0')
                {
                    if (nonzero) trailingZeros++;
                    continue;
                }
                nonzero = true;
                if (digits.Length + trailingZeros + 1 > expected.Digits.Length) return false;
                digits.Append('0', (int)trailingZeros);
                trailingZeros = 0;
                digits.Append((char)c);
            }
            if (!nonzero) return expected.Digits.Length == 0;
            if (expected.Digits.Length == 0 || negative != expected.Negative || digits.ToString() != expected.Digits) return false;
            BigInteger exponent = BigInteger.Zero;
            if (reader.Position < node.ValueEnd)
            {
                reader.Read();
                bool minus = reader.Consume('-');
                if (!minus) reader.Consume('+');
                var exp = new StringBuilder();
                int limit = Math.Max(20, expected.Exponent.ToString(CultureInfo.InvariantCulture).Length) + 2;
                while (reader.Position < node.ValueEnd)
                {
                    int c = reader.Read();
                    if (c == '0' && exp.Length == 0) continue;
                    if (exp.Length >= limit) return false;
                    exp.Append((char)c);
                }
                if (exp.Length != 0) exponent = BigInteger.Parse(exp.ToString(), CultureInfo.InvariantCulture);
                if (minus) exponent = -exponent;
            }
            return exponent - fractionDigits + trailingZeros == expected.Exponent;
        }

        private bool RadixEquals(StructureNode node, NumberValue expected)
        {
            reader.Read();
            int radix = reader.Read() switch { 'x' => 16, 'o' => 8, _ => 2 };
            if (expected.Negative || expected.Exponent < 0) return false;
            if (expected.Exponent > MaxExpressionLength)
                throw new StructureQueryException("The radix filter exponent exceeds the bounded comparison workspace.", 0);
            BigInteger wanted = expected.Digits.Length == 0 ? BigInteger.Zero
                : BigInteger.Parse(expected.Digits, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)expected.Exponent);
            BigInteger actual = BigInteger.Zero;
            while (reader.Position < node.ValueEnd)
            {
                actual = actual * radix + ConfigScalarText.Hex(reader.Read());
                if (actual > wanted) return false;
            }
            return actual == wanted;
        }
    }
}

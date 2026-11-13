using System.Buffers;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Data.Structure;

public static class StructureEdits
{
    public const int MaxInputChars = 1_048_576;

    public static ByteEdit CreateEdit(IStructureIndex index, StructureNode node, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(value);
        ct.ThrowIfCancellationRequested();
        if (value.Length > MaxInputChars)
            throw new ArgumentException($"The new value cannot exceed {MaxInputChars} UTF-16 characters.", nameof(value));
        if (node.Id < 0 || node.Id >= index.Count || index.ReadNode(node.Id) != node)
            throw new ArgumentException("The node does not belong to this structural index.", nameof(node));
        if (node.Kind == StructureKind.XmlElement) node = ElementValue(index, node, ct);
        if (node.ValueStart < index.BomLength || node.ValueEnd < node.ValueStart || node.ValueEnd > index.Source.ByteLength)
            throw new InvalidDataException("The structural value range is outside the source snapshot.");

        var output = new EncodedWriter(index.Codec, value.Length, ct);
        switch (node.Kind)
        {
            case StructureKind.JsonString when index.Format is StructureFormat.Json or StructureFormat.Jsonc:
                ValidateCharacters(value, node, xml: false, ct);
                JsonString(output, value, ct);
                break;
            case StructureKind.JsonNumber when index.Format is StructureFormat.Json or StructureFormat.Jsonc:
                if (!IsNumber(value, ct)) throw Invalid(node, "Enter one JSON number without surrounding whitespace.");
                output.Append(value);
                break;
            case StructureKind.JsonBoolean when index.Format is StructureFormat.Json or StructureFormat.Jsonc:
                if (value is not ("true" or "false")) throw Invalid(node, "Enter the JSON literal true or false.");
                output.Append(value);
                break;
            case StructureKind.JsonNull when index.Format is StructureFormat.Json or StructureFormat.Jsonc:
                if (value != "null") throw Invalid(node, "Enter the JSON literal null.");
                output.Append(value);
                break;
            case StructureKind.XmlAttribute when index.Format == StructureFormat.Xml:
                WriteXml(output, value, node, AttributeQuote(index, node, ct), cdata: false, ct);
                break;
            case StructureKind.XmlText when index.Format == StructureFormat.Xml:
                if (node.ParentId < 0 || index.ReadNode(node.ParentId).Kind != StructureKind.XmlElement)
                    throw Unsupported(node, "Whitespace outside the XML root is not an editable primitive value.");
                WriteXml(output, value, node, quote: null, cdata: false, ct);
                break;
            case StructureKind.XmlCData when index.Format == StructureFormat.Xml:
                WriteXml(output, value, node, quote: null, cdata: true, ct);
                break;
            default:
                throw Unsupported(node, "Only JSON primitive values and XML attribute, text or CDATA values can be edited.");
        }
        ct.ThrowIfCancellationRequested();
        return new ByteEdit(node.ValueRange, output.Finish());
    }

    private static StructureNode ElementValue(IStructureIndex index, StructureNode element, CancellationToken ct)
    {
        StructureNode? value = null;
        for (long id = element.Id + 1; id <= element.LastDescendantId;)
        {
            ct.ThrowIfCancellationRequested();
            var child = index.ReadNode(id);
            if (child.ParentId != element.Id || child.LastDescendantId < id || child.LastDescendantId > element.LastDescendantId)
                throw new InvalidDataException("The structural index contains an invalid XML child link.");
            if (child.Kind == StructureKind.XmlElement)
                throw Unsupported(element, "XML elements with element children cannot be edited as primitive values.");
            if (child.Kind is StructureKind.XmlText or StructureKind.XmlCData)
            {
                if (value.HasValue) throw Unsupported(element, "The XML element must contain exactly one text or CDATA value.");
                value = child;
            }
            id = child.LastDescendantId + 1;
        }
        return value ?? throw Unsupported(element, "The XML element has no single text or CDATA value.");
    }

    private static char AttributeQuote(IStructureIndex index, StructureNode node, CancellationToken ct)
    {
        int width = index.Codec.Alignment;
        if (node.ValueStart - width < index.BomLength)
            throw new InvalidDataException("The XML attribute has no opening quote.");
        Span<byte> bytes = stackalloc byte[4];
        int read = 0;
        while (read < width)
        {
            ct.ThrowIfCancellationRequested();
            int count = index.Source.Read(node.ValueStart - width + read, bytes.Slice(read, width - read));
            if (count <= 0) throw new EndOfStreamException("The XML attribute quote is unavailable.");
            read += count;
        }
        string quote = index.Codec.GetString(bytes[..width]);
        if (quote.Length != 1 || quote[0] is not ('\'' or '"'))
            throw new InvalidDataException("The XML attribute has no opening quote.");
        return quote[0];
    }

    private static void JsonString(EncodedWriter output, string value, CancellationToken ct)
    {
        output.Append("\"");
        for (int at = 0; at < value.Length;)
        {
            ct.ThrowIfCancellationRequested();
            int length = Math.Min(4096, value.Length - at);
            if (at + length < value.Length && char.IsHighSurrogate(value[at + length - 1])) length--;
            string quoted = JsonSplice.Quote(value.Substring(at, length));
            output.Append(quoted.AsSpan(1, quoted.Length - 2));
            at += length;
        }
        output.Append("\"");
    }

    private static bool IsNumber(string value, CancellationToken ct)
    {
        int at = 0;
        bool Consume(char c)
        {
            if (at == value.Length || value[at] != c) return false;
            at++;
            return true;
        }
        bool Digits()
        {
            int start = at;
            while (at < value.Length && value[at] is >= '0' and <= '9')
            {
                if ((at & 4095) == 0) ct.ThrowIfCancellationRequested();
                at++;
            }
            return at != start;
        }
        Consume('-');
        if (Consume('0'))
        {
            if (at < value.Length && value[at] is >= '0' and <= '9') return false;
        }
        else if (!Digits()) return false;
        if (Consume('.') && !Digits()) return false;
        if (at < value.Length && value[at] is 'e' or 'E')
        {
            at++;
            if (!Consume('+')) Consume('-');
            if (!Digits()) return false;
        }
        return at == value.Length;
    }

    private static void ValidateCharacters(string value, StructureNode node, bool xml, CancellationToken ct)
    {
        int untilCheck = 0;
        for (int at = 0; at < value.Length;)
        {
            if (untilCheck-- <= 0) { ct.ThrowIfCancellationRequested(); untilCheck = 2047; }
            if (Rune.DecodeFromUtf16(value.AsSpan(at), out var rune, out int used) != OperationStatus.Done)
                throw Invalid(node, "The new value contains an unpaired Unicode surrogate.");
            if (xml && !XmlCharacter(rune.Value)) throw Invalid(node, "The new value contains an invalid XML 1.0 character.");
            at += used;
        }
    }

    private static bool XmlCharacter(int c) => c is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or
        >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF;

    private static void WriteXml(EncodedWriter output, string value, StructureNode node, char? quote, bool cdata, CancellationToken ct)
    {
        ValidateCharacters(value, node, xml: true, ct);
        bool insideCdata = cdata;
        for (int at = 0; at < value.Length;)
        {
            if ((at & 2047) == 0) ct.ThrowIfCancellationRequested();
            if (insideCdata && value.AsSpan(at).StartsWith("]]>"))
            {
                output.Append("]]]]><![CDATA[>");
                at += 3;
                continue;
            }
            Rune.DecodeFromUtf16(value.AsSpan(at), out var rune, out int used);
            if (insideCdata && rune.Value == '\r')
            {
                output.Append("]]>");
                insideCdata = false;
            }
            string? escaped = insideCdata ? null : rune.Value switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' when !quote.HasValue => "&gt;",
                '\'' when quote == '\'' => "&apos;",
                '"' when quote == '"' => "&quot;",
                '\t' when quote.HasValue => "&#x9;",
                '\n' when quote.HasValue => "&#xA;",
                '\r' => "&#xD;",
                _ => null,
            };
            if (escaped != null) output.Append(escaped);
            else output.Append(value.AsSpan(at, used));
            at += used;
        }
        if (cdata && !insideCdata) output.Append("<![CDATA[");
    }

    private static StructureSyntaxException Invalid(StructureNode node, string message) =>
        new(StructureIssueKind.Syntax, node.ValueStart, node.ValueEnd, message);
    private static StructureSyntaxException Unsupported(StructureNode node, string message) =>
        new(StructureIssueKind.UnsupportedSyntax, node.Start, node.End, message);

    private sealed class EncodedWriter(TextCodec codec, int inputLength, CancellationToken ct)
    {
        private const int ChunkChars = 4096;
        private readonly StringBuilder chars = new(ChunkChars);
        private byte[]? bytes;
        private int length;

        public void Append(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                ct.ThrowIfCancellationRequested();
                int take = Math.Min(ChunkChars - chars.Length, text.Length);
                if (take > 0 && take < text.Length && char.IsHighSurrogate(text[take - 1])) take--;
                if (take > 0) { chars.Append(text[..take]); text = text[take..]; }
                if (chars.Length == ChunkChars || take == 0) Flush();
            }
        }

        private void Flush()
        {
            ct.ThrowIfCancellationRequested();
            if (chars.Length == 0) return;
            byte[] encoded = codec.Encode(chars.ToString());
            ct.ThrowIfCancellationRequested();
            // At most six characters per input unit, plus quotes or one CDATA-to-text transition.
            // Allocate once, so even the UTF-32 escape case stays below 25 MiB without a second output copy.
            int width = codec.Kind switch { CodecKind.SingleByte => 1, CodecKind.Utf16 => 2, _ => 4 };
            bytes ??= new byte[checked((inputLength * 6 + 14) * width)];
            encoded.CopyTo(bytes.AsSpan(length));
            length += encoded.Length;
            chars.Clear();
        }

        public ReadOnlyMemory<byte> Finish()
        {
            Flush();
            ct.ThrowIfCancellationRequested();
            return bytes == null ? ReadOnlyMemory<byte>.Empty : bytes.AsMemory(0, length);
        }
    }
}
